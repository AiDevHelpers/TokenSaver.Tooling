using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TokenSaver.Vs.Extension.Health;

/// <summary>Thin HTTP/TCP health-check helper for the already-running TokenSaver.MCP server.</summary>
public static class HealthCheckClient
{
    public static async Task<bool> WaitForPortOpenAsync(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsPortOpenAsync(host, port).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }

        return false;
    }

    public static async Task<bool> VerifyMcpServerAsync(int port)
    {
        var report = await GetFullHealthAsync(port).ConfigureAwait(false);
        return string.Equals(report?.Status, "healthy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(report?.Status, "ok", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<McpHealthReport?> GetFullHealthAsync(int port)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            return await httpClient
                .GetFromJsonAsync<McpHealthReport>($"http://localhost:{port}/health")
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
