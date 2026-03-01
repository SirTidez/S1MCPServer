#if !MONO
using System.Text.Json.Serialization;
#endif

namespace S1MCPServer.Models;

/// <summary>
/// Represents an acknowledgment message from the client.
/// </summary>
public class Acknowledgment
{
    /// <summary>
    /// Request ID that this acknowledgment corresponds to.
    /// </summary>
#if !MONO
    [JsonPropertyName("id")]
#endif
    public int Id { get; set; }

    /// <summary>
    /// Acknowledgment status.
    /// </summary>
#if !MONO
    [JsonPropertyName("status")]
#endif
    public string Status { get; set; } = "received";
}


