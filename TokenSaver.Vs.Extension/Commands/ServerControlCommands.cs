using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.WindowsService;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>Shared helpers for the server control commands. The MCP server runs as the TokenSaverMCP service.</summary>
internal static class ServerControl
{
    public const string ServiceName = "TokenSaverMCP";

    public static string LogsDirectory =>
        Path.Combine(Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData", "TokenSaver", "logs");

    public static Task ShowAsync(VisualStudioExtensibility extensibility, string message, CancellationToken ct) =>
        extensibility.Shell().ShowPromptAsync(message, PromptOptions.OK, ct);
}

/// <summary>Stops the TokenSaver MCP service.</summary>
[VisualStudioContribution]
public sealed class StopServerCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Stop TokenSaver MCP Server");

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        ExtensionLog.Logger.LogInfo("Stop TokenSaver MCP Server requested");
        if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServerControl.ServiceName).ConfigureAwait(false))
        {
            await ServerControl.ShowAsync(this.Extensibility, "TokenSaver MCP service is not installed.", cancellationToken).ConfigureAwait(false);
            return;
        }

        await WindowsServiceHelper.StopServiceAsync(ServerControl.ServiceName).ConfigureAwait(false);
        await ServerControl.ShowAsync(this.Extensibility, "TokenSaver MCP server stopped.", cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Stops and then starts the TokenSaver MCP service.</summary>
[VisualStudioContribution]
public sealed class RestartServerCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Restart TokenSaver MCP Server");

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        ExtensionLog.Logger.LogInfo("Restart TokenSaver MCP Server requested");
        if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServerControl.ServiceName).ConfigureAwait(false))
        {
            await ServerControl.ShowAsync(this.Extensibility, "TokenSaver MCP service is not installed.", cancellationToken).ConfigureAwait(false);
            return;
        }

        await WindowsServiceHelper.StopServiceAsync(ServerControl.ServiceName).ConfigureAwait(false);
        await WindowsServiceHelper.StartServiceAsync(ServerControl.ServiceName).ConfigureAwait(false);
        await ServerControl.ShowAsync(this.Extensibility, "TokenSaver MCP server restarted.", cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Reports whether the TokenSaver MCP service is installed and whether its HTTP port answers.</summary>
[VisualStudioContribution]
public sealed class ShowServerStatusCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Show TokenSaver Status");

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var installed = await WindowsServiceHelper.IsServiceInstalledAsync(ServerControl.ServiceName).ConfigureAwait(false);
        var reachable = installed && await CheckReachableAsync().ConfigureAwait(false);

        var message = !installed
            ? "TokenSaver MCP service: not installed."
            : reachable
                ? "TokenSaver MCP service: installed, server responding."
                : "TokenSaver MCP service: installed, server not responding.";

        await ServerControl.ShowAsync(this.Extensibility, message, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> CheckReachableAsync()
    {
        using var extension = new TokenSaverExtension(Environment.CurrentDirectory, ExtensionLog.Logger);
        return await extension.CheckServerHealthAsync().ConfigureAwait(false);
    }
}

/// <summary>Opens the TokenSaver logs folder in Windows Explorer.</summary>
[VisualStudioContribution]
public sealed class ShowLogsCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Show TokenSaver Logs");

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var folder = ServerControl.LogsDirectory;
        Directory.CreateDirectory(folder);
        _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false });
        return Task.CompletedTask;
    }
}
