using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MelonLoader;
using MelonLoader.Utils;
using S1MCPServer.Core;
using S1MCPServer.Models;
using S1MCPServer.Utils;

namespace S1MCPServer.Handlers;

/// <summary>
/// Handles log capture and filtering commands.
/// </summary>
public class LogCommandHandler : ICommandHandler
{
    private readonly ResponseQueue _responseQueue;

    public LogCommandHandler(ResponseQueue responseQueue)
    {
        _responseQueue = responseQueue;
    }

    public void Handle(Request request)
    {
        switch (request.Method)
        {
            case "capture_logs":
                HandleCaptureLogs(request);
                break;
            default:
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32601, // Method not found
                    $"Unknown log method: {request.Method}"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                break;
        }
    }

    private void HandleCaptureLogs(Request request)
    {
        try
        {
            ModLogger.Debug("Capturing logs");

            // Parse parameters
            var params_ = request.Params ?? new Dictionary<string, object>();
            
            int? lastNLines = GetIntParam(params_, "last_n_lines");
            int? firstNLines = GetIntParam(params_, "first_n_lines");
            string? keyword = GetStringParam(params_, "keyword");
            string? fromTimestamp = GetStringParam(params_, "from_timestamp");
            string? toTimestamp = GetStringParam(params_, "to_timestamp");
            string? includePattern = GetStringParam(params_, "include_pattern");
            string? excludePattern = GetStringParam(params_, "exclude_pattern");

            // Validate mutually exclusive parameters
            if (lastNLines.HasValue && firstNLines.HasValue)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "Cannot specify both last_n_lines and first_n_lines"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Get log file path
            string logPath = Path.Combine(MelonEnvironment.MelonLoaderDirectory, "Latest.log");
            ModLogger.Debug($"Log file path: {logPath}");

            if (!File.Exists(logPath))
            {
                ModLogger.Warn($"Log file not found: {logPath}");
                var result = new Dictionary<string, object>
                {
                    ["lines"] = new List<object>(),
                    ["total_lines_in_file"] = 0,
                    ["filtered_count"] = 0,
                    ["filters_applied"] = new List<string>(),
                    ["warning"] = "Log file not found"
                };
                var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
                _responseQueue.EnqueueResponse(response);
                return;
            }

            // Read all lines from the log file
            List<LogLine> logLines;
            try
            {
                // Try reading with shared access to avoid lock issues
                using var fileStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fileStream);
                
                logLines = new List<LogLine>();
                int lineNumber = 1;
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    logLines.Add(new LogLine
                    {
                        LineNumber = lineNumber++,
                        Content = line,
                        Timestamp = ExtractTimestamp(line)
                    });
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Error reading log file: {ex.Message}");
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    "Failed to read log file",
                    new { details = ex.Message }
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            int totalLines = logLines.Count;
            var filtersApplied = new List<string>();

            // Apply filters in order: timestamp → keyword → regex → line count
            
            // 1. Timestamp filtering
            if (!string.IsNullOrEmpty(fromTimestamp) || !string.IsNullOrEmpty(toTimestamp))
            {
                logLines = FilterByTimestamp(logLines, fromTimestamp, toTimestamp, filtersApplied);
            }

            // 2. Keyword search
            if (!string.IsNullOrEmpty(keyword))
            {
                logLines = logLines.Where(l => 
                    l.Content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                ).ToList();
                filtersApplied.Add($"keyword: {keyword}");
            }

            // 3. Regex filtering
            if (!string.IsNullOrEmpty(includePattern))
            {
                try
                {
                    var regex = new Regex(includePattern, RegexOptions.IgnoreCase);
                    logLines = logLines.Where(l => regex.IsMatch(l.Content)).ToList();
                    filtersApplied.Add($"include_pattern: {includePattern}");
                }
                catch (ArgumentException ex)
                {
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32602,
                        "Invalid include_pattern regex",
                        new { details = ex.Message }
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }
            }

            if (!string.IsNullOrEmpty(excludePattern))
            {
                try
                {
                    var regex = new Regex(excludePattern, RegexOptions.IgnoreCase);
                    logLines = logLines.Where(l => !regex.IsMatch(l.Content)).ToList();
                    filtersApplied.Add($"exclude_pattern: {excludePattern}");
                }
                catch (ArgumentException ex)
                {
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32602,
                        "Invalid exclude_pattern regex",
                        new { details = ex.Message }
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }
            }

            int filteredCount = logLines.Count;

            // 4. Line count limiting
            if (lastNLines.HasValue)
            {
                logLines = logLines.Skip(Math.Max(0, logLines.Count - lastNLines.Value)).ToList();
                filtersApplied.Add($"last_n_lines: {lastNLines.Value}");
            }
            else if (firstNLines.HasValue)
            {
                logLines = logLines.Take(firstNLines.Value).ToList();
                filtersApplied.Add($"first_n_lines: {firstNLines.Value}");
            }

            // Build response
            var resultLines = logLines.Select(l => new Dictionary<string, object>
            {
                ["line_number"] = l.LineNumber,
                ["timestamp"] = l.Timestamp ?? "",
                ["content"] = l.Content
            }).ToList();

            var finalResult = new Dictionary<string, object>
            {
                ["lines"] = resultLines,
                ["total_lines_in_file"] = totalLines,
                ["filtered_count"] = filteredCount,
                ["filters_applied"] = filtersApplied
            };

            ModLogger.Debug($"Captured {resultLines.Count} log lines (from {totalLines} total, {filteredCount} after filtering)");

            var successResponse = ProtocolHandler.CreateSuccessResponse(request.Id, finalResult);
            _responseQueue.EnqueueResponse(successResponse);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleCaptureLogs: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to capture logs",
                new { details = ex.Message, stack_trace = ex.StackTrace }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private List<LogLine> FilterByTimestamp(List<LogLine> lines, string? fromTimestamp, string? toTimestamp, List<string> filtersApplied)
    {
        TimeSpan? fromTime = null;
        TimeSpan? toTime = null;

        if (!string.IsNullOrEmpty(fromTimestamp))
        {
            if (TimeSpan.TryParse(fromTimestamp, out var parsedFrom))
            {
                fromTime = parsedFrom;
                filtersApplied.Add($"from_timestamp: {fromTimestamp}");
            }
            else
            {
                ModLogger.Warn($"Invalid from_timestamp format: {fromTimestamp}");
            }
        }

        if (!string.IsNullOrEmpty(toTimestamp))
        {
            if (TimeSpan.TryParse(toTimestamp, out var parsedTo))
            {
                toTime = parsedTo;
                filtersApplied.Add($"to_timestamp: {toTimestamp}");
            }
            else
            {
                ModLogger.Warn($"Invalid to_timestamp format: {toTimestamp}");
            }
        }

        if (fromTime.HasValue || toTime.HasValue)
        {
            return lines.Where(l =>
            {
                if (string.IsNullOrEmpty(l.Timestamp))
                    return false; // Skip lines without timestamps
                
                if (!TimeSpan.TryParse(l.Timestamp, out var lineTime))
                    return false; // Skip lines with invalid timestamps

                if (fromTime.HasValue && lineTime < fromTime.Value)
                    return false;
                
                if (toTime.HasValue && lineTime > toTime.Value)
                    return false;

                return true;
            }).ToList();
        }

        return lines;
    }

    private string? ExtractTimestamp(string line)
    {
        // MelonLoader format: [HH:mm:ss.fff]
        var match = Regex.Match(line, @"^\[(\d{2}:\d{2}:\d{2}\.\d{3})\]");
        return match.Success ? match.Groups[1].Value : null;
    }

    private int? GetIntParam(Dictionary<string, object> params_, string key)
    {
        if (params_.TryGetValue(key, out var value))
        {
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Number)
            {
                return jsonElement.GetInt32();
            }
            if (value is int intValue)
            {
                return intValue;
            }
            if (value is long longValue)
            {
                return (int)longValue;
            }
            if (int.TryParse(value?.ToString(), out var parsed))
            {
                return parsed;
            }
        }
        return null;
    }

    private string? GetStringParam(Dictionary<string, object> params_, string key)
    {
        if (params_.TryGetValue(key, out var value))
        {
            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
            {
                return jsonElement.GetString();
            }
            return value?.ToString();
        }
        return null;
    }

    private class LogLine
    {
        public int LineNumber { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? Timestamp { get; set; }
    }
}

