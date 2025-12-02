using System.Text.Json.Serialization;

namespace S1MCPServer.Models;

/// <summary>
/// Represents an acknowledgment message from the client.
/// </summary>
public class Acknowledgment
{
    /// <summary>
    /// Request ID that this acknowledgment corresponds to.
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// Acknowledgment status.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "received";
}


