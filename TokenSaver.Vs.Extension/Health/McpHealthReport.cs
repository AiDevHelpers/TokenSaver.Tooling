using System.Text.Json.Serialization;

namespace TokenSaver.Vs.Extension.Health;

/// <summary>Mirrors the shape of TokenSaver.MCP's /health response (see docs/tokensaver-mcp-health-contract.md).</summary>
public sealed class McpHealthReport
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
