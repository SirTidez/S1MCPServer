#if !MONO
using System.Text.Json.Serialization;
#endif

namespace S1MCPServer.Models;

/// <summary>
/// Represents a JSON-RPC request from the MCP server.
/// </summary>
public class Request
{
    /// <summary>
    /// Unique request identifier.
    /// </summary>
#if !MONO
    [JsonPropertyName("id")]
#endif
    public int Id { get; set; }

    /// <summary>
    /// Method name to invoke.
    /// </summary>
#if !MONO
    [JsonPropertyName("method")]
#endif
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Method parameters as a JSON object.
    /// </summary>
#if !MONO
    [JsonPropertyName("params")]
#endif
    public Dictionary<string, object>? Params { get; set; }
}


