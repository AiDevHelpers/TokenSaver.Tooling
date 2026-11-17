using System;
using System.IO;
using System.Threading.Tasks;

using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.WindowsService;

namespace TokenSaver.Vs.Extension.Services;

/// <summary>
/// Start, stop, restart and status of the MCP server, which runs as the TokenSaverMCP Windows service. Each method
/// returns a message for the Services page and shows nothing itself.
/// </summary>
internal static class ServerControl
{
    public const string ServiceName = "TokenSaverMCP";

    private const string NotInstalled = "TokenSaver MCP service is not installed.";

    public static string LogsDirectory =>
        Path.Combine(Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData", "TokenSaver", "logs");

    public static async Task<string> StartAsync()
    {
        ExtensionLog.Logger.LogInfo("Start TokenSaver MCP Server requested");
        if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServiceName).ConfigureAwait(false))
        {
            return NotInstalled;
        }

        await WindowsServiceHelper.StartServiceAsync(ServiceName).ConfigureAwait(false);
        return await IsRespondingAsync().ConfigureAwait(false)
            ? "TokenSaver MCP server is running and reachable."
            : "TokenSaver MCP server was started but is not responding yet. See the Logs tab.";
    }

    public static async Task<string> StopAsync()
    {
        ExtensionLog.Logger.LogInfo("Stop TokenSaver MCP Server requested");
        if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServiceName).ConfigureAwait(false))
        {
            return NotInstalled;
        }

        await WindowsServiceHelper.StopServiceAsync(ServiceName).ConfigureAwait(false);
        return "TokenSaver MCP server stopped.";
    }

    public static async Task<string> RestartAsync()
    {
        ExtensionLog.Logger.LogInfo("Restart TokenSaver MCP Server requested");
        if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServiceName).ConfigureAwait(false))
        {
            return NotInstalled;
        }

        await WindowsServiceHelper.StopServiceAsync(ServiceName).ConfigureAwait(false);
        await WindowsServiceHelper.StartServiceAsync(ServiceName).ConfigureAwait(false);
        return "TokenSaver MCP server restarted.";
    }

    /// <summary>One-line state of the MCP service: not installed, responding, or not responding.</summary>
    public static async Task<string> StatusAsync()
    {
        if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServiceName).ConfigureAwait(false))
        {
            return "Not installed";
        }

        return await IsRespondingAsync().ConfigureAwait(false)
            ? "Installed, server responding"
            : "Installed, server not responding";
    }

    public static async Task<bool> IsRespondingAsync(int port = 11436)
    {
        using var extension = new TokenSaverExtension(Environment.CurrentDirectory, ExtensionLog.Logger);
        return await extension.CheckServerHealthAsync(port).ConfigureAwait(false);
    }
}
