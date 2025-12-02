using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using S1MCPServer.Models;

namespace S1MCPServer.Core;

/// <summary>
/// Handles JSON-RPC protocol serialization and deserialization.
/// </summary>
public static class ProtocolHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 32 // Limit depth to prevent excessive nesting
    };

    /// <summary>
    /// Serializes a request to JSON string.
    /// </summary>
    /// <param name="request">The request to serialize.</param>
    /// <returns>JSON string representation.</returns>
    public static string SerializeRequest(Request request)
    {
        return JsonSerializer.Serialize(request, JsonOptions);
    }

    /// <summary>
    /// Deserializes a JSON string to a Request object.
    /// </summary>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>The deserialized Request object.</returns>
    /// <exception cref="JsonException">Thrown if JSON is invalid.</exception>
    public static Request DeserializeRequest(string json)
    {
        Utils.ModLogger.Debug($"DeserializeRequest: Deserializing JSON ({json.Length} chars): {json}");
        try
        {
            var request = JsonSerializer.Deserialize<Request>(json, JsonOptions);
            if (request == null)
            {
                Utils.ModLogger.Error("DeserializeRequest: Deserialization returned null");
                throw new JsonException("Failed to deserialize request (result was null)");
            }
            Utils.ModLogger.Debug($"DeserializeRequest: Successfully deserialized - ID={request.Id}, Method={request.Method}");
            return request;
        }
        catch (JsonException ex)
        {
            Utils.ModLogger.Error($"DeserializeRequest: JSON deserialization failed: {ex.Message}");
            Utils.ModLogger.Debug($"DeserializeRequest: JSON content: {json}");
            throw;
        }
    }

    /// <summary>
    /// Serializes a response to JSON string.
    /// </summary>
    /// <param name="response">The response to serialize.</param>
    /// <returns>JSON string representation.</returns>
    public static string SerializeResponse(Response response)
    {
        Utils.ModLogger.Debug($"SerializeResponse: Serializing response ID={response.Id}, has_error={response.Error != null}, has_result={response.Result != null}");
        
        // Sanitize the response to remove non-serializable types (like IntPtr) before serialization
        var sanitizedResponse = SanitizeForSerialization(response);
        
        try
        {
            string json = JsonSerializer.Serialize(sanitizedResponse, JsonOptions);
            Utils.ModLogger.Debug($"SerializeResponse: Serialized to {json.Length} chars: {json}");
            return json;
        }
        catch (NotSupportedException ex) when (ex.Message.Contains("IntPtr"))
        {
            Utils.ModLogger.Error($"SerializeResponse: IntPtr detected in response - sanitization may have missed it. Error: {ex.Message}");
            // Try to sanitize more aggressively and retry
            var moreAggressiveSanitized = SanitizeForSerialization(sanitizedResponse, true);
            return JsonSerializer.Serialize(moreAggressiveSanitized, JsonOptions);
        }
    }

    /// <summary>
    /// Recursively sanitizes an object graph to remove non-serializable types like IntPtr.
    /// </summary>
    private static object? SanitizeForSerialization(object? obj, bool aggressive = false)
    {
        if (obj == null)
            return null;

        var type = obj.GetType();

        // Check if the type itself is IntPtr
        if (type == typeof(IntPtr) || type == typeof(UIntPtr))
            return aggressive ? "[IntPtr - not serializable]" : null;

        // Handle dictionaries
        if (obj is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = entry.Key?.ToString();
                if (key == null) continue;

                var sanitizedValue = SanitizeForSerialization(entry.Value, aggressive);
                if (sanitizedValue != null || aggressive)
                {
                    result[key] = sanitizedValue;
                }
            }
            return result;
        }

        // Handle generic dictionaries
        if (obj is IDictionary<object, object?> genericDict)
        {
            var result = new Dictionary<string, object?>();
            foreach (var kvp in genericDict)
            {
                var key = kvp.Key?.ToString();
                if (key == null) continue;

                var sanitizedValue = SanitizeForSerialization(kvp.Value, aggressive);
                if (sanitizedValue != null || aggressive)
                {
                    result[key] = sanitizedValue;
                }
            }
            return result;
        }

        // Handle lists and collections
        if (obj is IEnumerable enumerable && !(obj is string))
        {
            var result = new List<object?>();
            foreach (var item in enumerable)
            {
                var sanitized = SanitizeForSerialization(item, aggressive);
                if (sanitized != null || aggressive)
                {
                    result.Add(sanitized);
                }
            }
            return result;
        }

        // For Response objects, sanitize the Result property
        if (obj is Response response)
        {
            return new Response
            {
                Id = response.Id,
                Result = SanitizeForSerialization(response.Result, aggressive),
                Error = response.Error
            };
        }

        // For primitives and strings, return as-is
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
            return obj;

        // For other objects, try to return as-is (they should be serializable)
        return obj;
    }

    /// <summary>
    /// Creates an error response.
    /// </summary>
    /// <param name="id">The request ID this error corresponds to.</param>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="data">Optional additional error data.</param>
    /// <returns>A Response object with the error.</returns>
    public static Response CreateErrorResponse(int id, int code, string message, object? data = null)
    {
        return new Response
        {
            Id = id,
            Result = null,
            Error = new ErrorResponse
            {
                Code = code,
                Message = message,
                Data = data
            }
        };
    }

    /// <summary>
    /// Creates a success response.
    /// </summary>
    /// <param name="id">The request ID this response corresponds to.</param>
    /// <param name="result">The result object.</param>
    /// <returns>A Response object with the result.</returns>
    public static Response CreateSuccessResponse(int id, object result)
    {
        return new Response
        {
            Id = id,
            Result = result,
            Error = null
        };
    }

    /// <summary>
    /// Deserializes an acknowledgment from a JSON string.
    /// </summary>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>The deserialized Acknowledgment object.</returns>
    /// <exception cref="JsonException">Thrown if JSON is invalid.</exception>
    public static Acknowledgment DeserializeAcknowledgment(string json)
    {
        Utils.ModLogger.Debug($"DeserializeAcknowledgment: Deserializing JSON ({json.Length} chars): {json}");
        try
        {
            var ack = JsonSerializer.Deserialize<Acknowledgment>(json, JsonOptions);
            if (ack == null)
            {
                Utils.ModLogger.Error("DeserializeAcknowledgment: Deserialization returned null");
                throw new JsonException("Failed to deserialize acknowledgment (result was null)");
            }
            Utils.ModLogger.Debug($"DeserializeAcknowledgment: Successfully deserialized - ID={ack.Id}, Status={ack.Status}");
            return ack;
        }
        catch (JsonException ex)
        {
            Utils.ModLogger.Error($"DeserializeAcknowledgment: JSON deserialization failed: {ex.Message}");
            Utils.ModLogger.Debug($"DeserializeAcknowledgment: JSON content: {json}");
            throw;
        }
    }

    /// <summary>
    /// Reads a message from a stream (length prefix + JSON).
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <returns>The JSON message string.</returns>
    /// <exception cref="IOException">Thrown if stream read fails.</exception>
    public static async Task<string> ReadMessageAsync(Stream stream)
    {
        Utils.ModLogger.Debug("ReadMessageAsync: Starting to read message from stream");
        
        // Check if stream is readable
        if (!stream.CanRead)
        {
            Utils.ModLogger.Error("ReadMessageAsync: Stream is not readable");
            throw new IOException("Stream is not readable");
        }
        
        // For NetworkStream, check if readable
        if (stream is System.Net.Sockets.NetworkStream networkStream)
        {
            if (!networkStream.CanRead)
            {
                Utils.ModLogger.Error("ReadMessageAsync: Network stream is not readable");
                throw new IOException("Network stream is not readable");
            }
            Utils.ModLogger.Debug($"ReadMessageAsync: Network stream is readable (CanRead: {networkStream.CanRead})");
        }
        
        // Read 4-byte length prefix
        byte[] lengthBuffer = new byte[4];
        Utils.ModLogger.Debug("ReadMessageAsync: Reading 4-byte length prefix...");
        int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4);
        Utils.ModLogger.Debug($"ReadMessageAsync: Read {bytesRead} bytes for length prefix");
        
        if (bytesRead == 0)
        {
            // Check if stream is still readable
            if (stream is System.Net.Sockets.NetworkStream networkStream2 && networkStream2.CanRead)
            {
                Utils.ModLogger.Debug("ReadMessageAsync: Read 0 bytes but stream is still readable - client may be waiting for response");
                // In request-response pattern, 0 bytes after sending a request is normal
                // The client is waiting for our response. Don't treat this as an error immediately.
                // Wait a bit longer to see if client sends another request
                await Task.Delay(100); // Wait a bit longer
                
                // Try reading again
                bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4);
                Utils.ModLogger.Debug($"ReadMessageAsync: After wait, read {bytesRead} bytes");
            }
            
            if (bytesRead == 0)
            {
                // Final check - if still readable, this might be normal in request-response pattern
                if (stream is System.Net.Sockets.NetworkStream networkStream3 && networkStream3.CanRead)
                {
                    Utils.ModLogger.Debug("ReadMessageAsync: Still 0 bytes but stream readable - treating as normal (client waiting for response)");
                    // Don't throw error - let the caller handle this
                    // The connection is still active, just no data yet
                    throw new IOException("No data available (client may be waiting for response)");
                }
                else
                {
                    Utils.ModLogger.Error("ReadMessageAsync: Stream returned 0 bytes (EOF/closed)");
                    throw new IOException("Stream closed or EOF reached (read 0 bytes)");
                }
            }
        }
        
        if (bytesRead != 4)
        {
            Utils.ModLogger.Error($"ReadMessageAsync: Failed to read message length (got {bytesRead} bytes instead of 4)");
            throw new IOException($"Failed to read message length (got {bytesRead} bytes instead of 4)");
        }

        int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
        Utils.ModLogger.Debug($"ReadMessageAsync: Message length from prefix: {messageLength} bytes");
        
        if (messageLength < 0 || messageLength > 10 * 1024 * 1024) // Max 10MB
        {
            Utils.ModLogger.Error($"ReadMessageAsync: Invalid message length: {messageLength}");
            throw new IOException($"Invalid message length: {messageLength}");
        }

        // Read JSON message
        byte[] messageBuffer = new byte[messageLength];
        int totalBytesRead = 0;
        int readAttempts = 0;
        
        while (totalBytesRead < messageLength)
        {
            readAttempts++;
            Utils.ModLogger.Debug($"ReadMessageAsync: Read attempt {readAttempts}, reading {messageLength - totalBytesRead} bytes...");
            bytesRead = await stream.ReadAsync(messageBuffer, totalBytesRead, messageLength - totalBytesRead);
            Utils.ModLogger.Debug($"ReadMessageAsync: Read {bytesRead} bytes in attempt {readAttempts}");
            
            if (bytesRead == 0)
            {
                Utils.ModLogger.Error($"ReadMessageAsync: Stream closed before message complete (read {totalBytesRead}/{messageLength} bytes)");
                throw new IOException($"Stream closed before message complete (read {totalBytesRead}/{messageLength} bytes)");
            }
            totalBytesRead += bytesRead;
        }

        Utils.ModLogger.Debug($"ReadMessageAsync: Successfully read {totalBytesRead} bytes");
        string jsonMessage = Encoding.UTF8.GetString(messageBuffer, 0, messageLength);
        Utils.ModLogger.Debug($"ReadMessageAsync: Decoded JSON message ({jsonMessage.Length} chars)");
        return jsonMessage;
    }

    /// <summary>
    /// Writes a message to a stream (length prefix + JSON).
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="json">The JSON message string.</param>
    /// <exception cref="IOException">Thrown if stream write fails.</exception>
    public static async Task WriteMessageAsync(Stream stream, string json)
    {
        Utils.ModLogger.Debug($"WriteMessageAsync: Starting to write message ({json.Length} chars)");
        
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        Utils.ModLogger.Debug($"WriteMessageAsync: Encoded to {jsonBytes.Length} bytes");
        
        byte[] lengthBytes = BitConverter.GetBytes(jsonBytes.Length);
        Utils.ModLogger.Debug($"WriteMessageAsync: Length prefix: {jsonBytes.Length} bytes");

        // Write length prefix
        Utils.ModLogger.Debug("WriteMessageAsync: Writing 4-byte length prefix...");
        await stream.WriteAsync(lengthBytes, 0, 4);
        Utils.ModLogger.Debug("WriteMessageAsync: Length prefix written");

        // Write JSON message
        Utils.ModLogger.Debug($"WriteMessageAsync: Writing {jsonBytes.Length} bytes of JSON message...");
        await stream.WriteAsync(jsonBytes, 0, jsonBytes.Length);
        Utils.ModLogger.Debug("WriteMessageAsync: JSON message written, flushing...");
        await stream.FlushAsync();
        Utils.ModLogger.Debug("WriteMessageAsync: Message written and flushed successfully");
    }
}


