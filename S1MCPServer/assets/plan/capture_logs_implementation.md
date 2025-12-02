# Capture Logs Implementation Documentation

## Overview

The `capture_logs` command enables agentic debugging by providing filtered access to MelonLoader's game logs. This command reads from the `Latest.log` file and supports multiple filtering options including keyword search, timestamp ranges, regex patterns, and line count limits.

## Command Specification

### Method Name
`capture_logs`

### Description
Captures and filters game logs from MelonLoader for debugging purposes. Retrieves logs from the game's `Latest.log` file with optional filtering capabilities.

## Parameters

All parameters are optional, allowing flexible querying of log data:

### Line Count Filters (Mutually Exclusive)

- **`last_n_lines`** (integer, optional)
  - Get the last N lines from the log file
  - Cannot be used with `first_n_lines`
  - Example: `100` (get last 100 lines)

- **`first_n_lines`** (integer, optional)
  - Get the first N lines from the log file
  - Cannot be used with `last_n_lines`
  - Example: `50` (get first 50 lines)

### Content Filters

- **`keyword`** (string, optional)
  - Case-insensitive substring search
  - Returns only lines containing this keyword
  - Example: `"error"`, `"S1MCPServer"`, `"NPC"`

- **`from_timestamp`** (string, optional)
  - Filter logs from this timestamp onwards
  - Format: `HH:mm:ss` or `HH:mm:ss.fff`
  - Example: `"12:30:45"` or `"12:30:45.123"`

- **`to_timestamp`** (string, optional)
  - Filter logs up to this timestamp
  - Format: `HH:mm:ss` or `HH:mm:ss.fff`
  - Example: `"12:35:00"` or `"12:35:00.999"`

- **`include_pattern`** (string, optional)
  - Regex pattern for including matching lines (case-insensitive)
  - Only lines matching this pattern will be returned
  - Example: `"ERROR|WARN"` (include errors or warnings)

- **`exclude_pattern`** (string, optional)
  - Regex pattern for excluding matching lines (case-insensitive)
  - Lines matching this pattern will be filtered out
  - Example: `"DEBUG"` (exclude debug messages)

## Response Format

The command returns a JSON object with the following structure:

```json
{
  "lines": [
    {
      "line_number": 1234,
      "timestamp": "12:34:56.789",
      "content": "[ERROR] Some error message"
    },
    {
      "line_number": 1235,
      "timestamp": "12:34:57.123",
      "content": "[INFO] S1MCPServer: Connection established"
    }
  ],
  "total_lines_in_file": 5000,
  "filtered_count": 25,
  "filters_applied": [
    "keyword: error",
    "from_timestamp: 12:30:00",
    "last_n_lines: 100"
  ],
  "warning": "Optional warning message if applicable"
}
```

### Response Fields

- **`lines`**: Array of log line objects containing:
  - `line_number`: Original line number in the log file
  - `timestamp`: Extracted timestamp (if available)
  - `content`: Full content of the log line

- **`total_lines_in_file`**: Total number of lines in the log file

- **`filtered_count`**: Number of lines after applying all filters (before line count limit)

- **`filters_applied`**: Array of strings describing which filters were applied

- **`warning`**: Optional field with warning messages (e.g., "Log file not found")

## Filter Application Order

Filters are applied in the following order for optimal performance:

1. **Timestamp filtering** - Filter by `from_timestamp` and/or `to_timestamp`
2. **Keyword search** - Filter by `keyword` substring match
3. **Regex filtering** - Apply `include_pattern` and/or `exclude_pattern`
4. **Line count limiting** - Apply `first_n_lines` or `last_n_lines`

This order ensures that expensive operations (like line count) are performed on already-filtered data.

## Usage Examples

### Example 1: Get Last 100 Lines

**Request:**
```json
{
  "id": 1,
  "method": "capture_logs",
  "params": {
    "last_n_lines": 100
  }
}
```

**Use Case:** Quick overview of recent activity

### Example 2: Search for Errors

**Request:**
```json
{
  "id": 2,
  "method": "capture_logs",
  "params": {
    "keyword": "error"
  }
}
```

**Use Case:** Find all error messages in the log

### Example 3: Time Range with Keywords

**Request:**
```json
{
  "id": 3,
  "method": "capture_logs",
  "params": {
    "from_timestamp": "12:30:00",
    "to_timestamp": "12:35:00",
    "keyword": "S1MCPServer"
  }
}
```

**Use Case:** Debug specific mod behavior during a time window

### Example 4: Regex Pattern Filtering

**Request:**
```json
{
  "id": 4,
  "method": "capture_logs",
  "params": {
    "include_pattern": "ERROR|WARN|EXCEPTION",
    "exclude_pattern": "DEBUG",
    "last_n_lines": 200
  }
}
```

**Use Case:** Find recent errors/warnings while excluding debug messages

### Example 5: Complex Debugging Scenario

**Request:**
```json
{
  "id": 5,
  "method": "capture_logs",
  "params": {
    "keyword": "NPC",
    "from_timestamp": "14:00:00",
    "include_pattern": ".*spawning.*|.*teleport.*",
    "last_n_lines": 50
  }
}
```

**Use Case:** Debug NPC spawning/teleportation issues after 2 PM

## Common Debugging Scenarios

### Scenario 1: Mod Not Loading
```json
{
  "keyword": "S1MCPServer",
  "first_n_lines": 100
}
```
Check if your mod initialized properly at startup.

### Scenario 2: Connection Issues
```json
{
  "keyword": "connection",
  "include_pattern": "error|failed|timeout",
  "last_n_lines": 200
}
```
Find recent connection-related errors.

### Scenario 3: Performance Investigation
```json
{
  "from_timestamp": "15:30:00",
  "to_timestamp": "15:35:00",
  "include_pattern": "slow|lag|performance|frame"
}
```
Investigate performance issues during a specific time window.

### Scenario 4: Exception Tracking
```json
{
  "include_pattern": "exception|stack trace",
  "last_n_lines": 500
}
```
Find all recent exceptions with their stack traces.

## Error Handling

### File Not Found
If the log file doesn't exist:
- Returns empty `lines` array
- `total_lines_in_file`: 0
- `warning`: "Log file not found"

### File Access Error
If the log file is locked or inaccessible:
- Returns error response with code `-32000`
- Error message: "Failed to read log file"
- Details: Specific error message

### Invalid Regex Pattern
If `include_pattern` or `exclude_pattern` contains invalid regex:
- Returns error response with code `-32602`
- Error message: "Invalid include_pattern regex" or "Invalid exclude_pattern regex"
- Details: Specific regex error

### Invalid Parameters
If both `last_n_lines` and `first_n_lines` are specified:
- Returns error response with code `-32602`
- Error message: "Cannot specify both last_n_lines and first_n_lines"

### Invalid Timestamp Format
If timestamp format is invalid:
- Filter is skipped with a warning logged
- Command continues with other filters

## Performance Considerations

### Log File Size
- Large log files (>10MB) may take a few seconds to read
- Filtering is performed in-memory for speed
- No caching is used (reads fresh data each time)

### Filter Optimization
- Use timestamp filters to reduce data processed by subsequent filters
- Combine `keyword` with `last_n_lines` for quick recent searches
- Regex patterns are compiled once per request

### Best Practices
1. Use `last_n_lines` for quick recent checks (< 1000 lines)
2. Combine timestamp filters with keywords for targeted searches
3. Use regex sparingly on large logs (can be slow)
4. Request only what you need to minimize data transfer

## Implementation Details

### C# Handler
- **File**: `S1MCPServer/Handlers/LogCommandHandler.cs`
- **Class**: `LogCommandHandler`
- **Method**: `HandleCaptureLogs(Request request)`

### Python Tool
- **File**: `S1MCPClient/src/tools/log_tools.py`
- **Tool Name**: `s1_capture_logs`
- **Handler**: `handle_s1_capture_logs(arguments, tcp_client)`

### Log File Path
- **Location**: `{MelonLoaderDirectory}/Latest.log`
- **Access**: `MelonEnvironment.MelonLoaderDirectory` property
- **Format**: MelonLoader standard log format with timestamps `[HH:mm:ss.fff]`

### Timestamp Parsing
- **Format**: `[HH:mm:ss.fff]` at the start of each line
- **Regex**: `^\[(\d{2}:\d{2}:\d{2}\.\d{3})\]`
- **Type**: `TimeSpan` for comparison

## Security Considerations

### Read-Only Operation
- This command only reads from the log file
- No write operations are performed
- Safe to use without concerns about modifying game state

### File Access
- Uses `FileShare.ReadWrite` to avoid locking issues
- Handles locked files gracefully
- No elevated permissions required

### Data Exposure
- Only exposes MelonLoader logs (already written to disk)
- No sensitive game data beyond what's already logged
- Useful for debugging without additional risk

## Future Enhancements

### Potential Improvements
1. **Streaming**: Support for real-time log streaming
2. **Multiple Files**: Access to older log files (not just Latest.log)
3. **Context Lines**: Include N lines before/after matches
4. **Aggregation**: Count occurrences of patterns
5. **Export**: Save filtered results to separate file
6. **Highlighting**: Mark matched keywords/patterns in results

### Performance Optimizations
1. **Pagination**: Support for paginated results on very large logs
2. **Caching**: Optional caching with cache invalidation
3. **Indexing**: Build index for faster timestamp searches
4. **Async Reading**: Asynchronous file reading for better responsiveness

## Troubleshooting

### No Results Returned
- Check if filters are too restrictive
- Verify timestamp format (HH:mm:ss or HH:mm:ss.fff)
- Test with fewer filters to isolate the issue

### Slow Performance
- Reduce line count with `last_n_lines` or `first_n_lines`
- Use timestamp filters to narrow the search window
- Avoid complex regex patterns on very large logs

### Missing Timestamps
- Some log lines may not have timestamps
- Lines without timestamps will be excluded from timestamp filtering
- Use other filters (keyword, regex) for these lines

## Related Commands

- **`get_game_state`**: Get current game state information
- **`list_methods`**: List all available MCP methods
- **Debug commands**: Various debug inspection commands

## Version History

- **v1.0** (2025-01-27): Initial implementation
  - Basic log capture functionality
  - Timestamp, keyword, and regex filtering
  - Line count limiting

---

**Last Updated**: 2025-01-27  
**Author**: Tyler (with AI assistance)

