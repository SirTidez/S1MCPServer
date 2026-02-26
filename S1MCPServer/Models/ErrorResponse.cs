#if !MONO
using System.Text.Json.Serialization;
#endif

namespace S1MCPServer.Models;

/// <summary>
/// Represents a JSON-RPC error response.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Error code (JSON-RPC standard or custom).
    /// </summary>
#if !MONO
    [JsonPropertyName("code")]
#endif
    public int Code { get; set; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
#if !MONO
    [JsonPropertyName("message")]
#endif
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Additional error data (optional).
    /// </summary>
#if !MONO
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
#endif
    public object? Data { get; set; }
}


