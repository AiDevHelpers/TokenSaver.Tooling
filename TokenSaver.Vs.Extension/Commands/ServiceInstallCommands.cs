using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.WindowsService;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>Shared install/uninstall flow for the two bundled service installers. Returns a message, shows nothing.</summary>
internal static class ServiceInstall
{
    public const string SimpleModelServerServiceName = "SimpleModelServer";
    public const string McpInstallerFile = "TokenSaver-Setup.msi";
    public const string SimpleModelServerInstallerFile = "SimpleModelServer-Setup.msi";

    public static async Task<(bool ok, string message)> InstallAsync(string label, string serviceName, string msiFile)
    {
        ExtensionLog.Logger.LogInfo($"Install {label} service requested");
        if (await WindowsServiceHelper.IsServiceInstalledAsync(serviceName).ConfigureAwait(false))
        {
            return (true, $"{label} service is already installed.");
        }

        var msi = MsiInstaller.BundledMsiPath(msiFile);
        if (!File.Exists(msi))
        {
            return (false, $"{label} installer is not bundled with this build: {msi}");
        }

        var code = await MsiInstaller.RunAsync("/i", msi).ConfigureAwait(false);
        ExtensionLog.Logger.LogInfo($"Install {label} service finished with code {code}");
        return code == 0
            ? (true, $"{label} service installed.")
            : (false, $"{label} install failed (msiexec exit code {code}). Approve the UAC prompt and try again.");
    }

    public static async Task<(bool ok, string message)> UninstallAsync(string label, string serviceName, string msiFile)
    {
        ExtensionLog.Logger.LogInfo($"Uninstall {label} service requested");
        if (!await WindowsServiceHelper.IsServiceInstalledAsync(serviceName).ConfigureAwait(false))
        {
            return (true, $"{label} service is not installed.");
        }

        var msi = MsiInstaller.BundledMsiPath(msiFile);
        if (!File.Exists(msi))
        {
            return (false, $"{label} uninstaller is not bundled with this build: {msi}");
        }

        var code = await MsiInstaller.RunAsync("/x", msi).ConfigureAwait(false);
        ExtensionLog.Logger.LogInfo($"Uninstall {label} service finished with code {code}");
        return code == 0
            ? (true, $"{label} service uninstalled.")
            : (false, $"{label} uninstall failed (msiexec exit code {code}). Approve the UAC prompt and try again.");
    }
}

/// <summary>Installs the TokenSaver MCP Windows service (TokenSaverMCP) from the bundled MSI.</summary>
[VisualStudioContribution]
public sealed class InstallMcpServiceCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("%InstallMcpService.DisplayName%") { Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu] };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var (_, message) = await ServiceInstall.InstallAsync("TokenSaver MCP", ServerControl.ServiceName, ServiceInstall.McpInstallerFile).ConfigureAwait(false);
        await ServerControl.ShowAsync(this.Extensibility, message, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Uninstalls the TokenSaver MCP Windows service (TokenSaverMCP) via the bundled MSI.</summary>
[VisualStudioContribution]
public sealed class UninstallMcpServiceCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("%UninstallMcpService.DisplayName%") { Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu] };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var (_, message) = await ServiceInstall.UninstallAsync("TokenSaver MCP", ServerControl.ServiceName, ServiceInstall.McpInstallerFile).ConfigureAwait(false);
        await ServerControl.ShowAsync(this.Extensibility, message, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Installs the SimpleModelServer Windows service from the bundled MSI.</summary>
[VisualStudioContribution]
public sealed class InstallSimpleModelServerCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("%InstallSimpleModelServer.DisplayName%") { Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu] };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var (_, message) = await ServiceInstall.InstallAsync("SimpleModelServer", ServiceInstall.SimpleModelServerServiceName, ServiceInstall.SimpleModelServerInstallerFile).ConfigureAwait(false);
        await ServerControl.ShowAsync(this.Extensibility, message, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Uninstalls the SimpleModelServer Windows service via the bundled MSI.</summary>
[VisualStudioContribution]
public sealed class UninstallSimpleModelServerCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("%UninstallSimpleModelServer.DisplayName%") { Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu] };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var (_, message) = await ServiceInstall.UninstallAsync("SimpleModelServer", ServiceInstall.SimpleModelServerServiceName, ServiceInstall.SimpleModelServerInstallerFile).ConfigureAwait(false);
        await ServerControl.ShowAsync(this.Extensibility, message, cancellationToken).ConfigureAwait(false);
    }
}
