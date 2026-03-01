#if !MONO
using System.Text.Json.Serialization;
#endif

namespace S1MCPServer.Models;

/// <summary>
/// Represents a JSON-RPC response to send back to the MCP server.
/// </summary>
public class Response
{
    /// <summary>
    /// Request ID that this response corresponds to.
    /// </summary>
#if !MONO
    [JsonPropertyName("id")]
#endif
    public int Id { get; set; }

    /// <summary>
    /// Result object (null if error occurred).
    /// </summary>
#if !MONO
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
#endif
    public object? Result { get; set; }

    /// <summary>
    /// Error object (null if successful).
    /// </summary>
#if !MONO
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
#endif
    public ErrorResponse? Error { get; set; }
}


