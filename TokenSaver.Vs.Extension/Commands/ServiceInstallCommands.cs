using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.WindowsService;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>Shared install/uninstall flow for the two bundled service installers.</summary>
internal static class ServiceInstall
{
    public const string SimpleModelServerServiceName = "SimpleModelServer";
    public const string McpInstallerFile = "TokenSaver-Setup.msi";
    public const string SimpleModelServerInstallerFile = "SimpleModelServer-Setup.msi";

    public static async Task InstallAsync(
        VisualStudioExtensibility extensibility, string label, string serviceName, string msiFile, CancellationToken ct)
    {
        ExtensionLog.Logger.LogInfo($"Install {label} service requested");
        if (await WindowsServiceHelper.IsServiceInstalledAsync(serviceName).ConfigureAwait(false))
        {
            await ServerControl.ShowAsync(extensibility, $"{label} service is already installed.", ct).ConfigureAwait(false);
            return;
        }

        var msi = MsiInstaller.BundledMsiPath(msiFile);
        if (!File.Exists(msi))
        {
            await ServerControl.ShowAsync(extensibility, $"{label} installer is not bundled with this build: {msi}", ct).ConfigureAwait(false);
            return;
        }

        var code = await MsiInstaller.RunAsync("/i", msi).ConfigureAwait(false);
        var message = code == 0
            ? $"{label} service installed. A UAC prompt was shown for elevation."
            : $"{label} install failed (msiexec exit code {code}). Approve the UAC prompt and try again.";
        ExtensionLog.Logger.LogInfo($"Install {label} service finished with code {code}");
        await ServerControl.ShowAsync(extensibility, message, ct).ConfigureAwait(false);
    }

    public static async Task UninstallAsync(
        VisualStudioExtensibility extensibility, string label, string serviceName, string msiFile, CancellationToken ct)
    {
        ExtensionLog.Logger.LogInfo($"Uninstall {label} service requested");
        if (!await WindowsServiceHelper.IsServiceInstalledAsync(serviceName).ConfigureAwait(false))
        {
            await ServerControl.ShowAsync(extensibility, $"{label} service is not installed.", ct).ConfigureAwait(false);
            return;
        }

        var msi = MsiInstaller.BundledMsiPath(msiFile);
        if (!File.Exists(msi))
        {
            await ServerControl.ShowAsync(extensibility, $"{label} uninstaller is not bundled with this build: {msi}", ct).ConfigureAwait(false);
            return;
        }

        var code = await MsiInstaller.RunAsync("/x", msi).ConfigureAwait(false);
        var message = code == 0
            ? $"{label} service uninstalled."
            : $"{label} uninstall failed (msiexec exit code {code}). Approve the UAC prompt and try again.";
        ExtensionLog.Logger.LogInfo($"Uninstall {label} service finished with code {code}");
        await ServerControl.ShowAsync(extensibility, message, ct).ConfigureAwait(false);
    }
}

/// <summary>Installs the TokenSaver MCP Windows service (TokenSaverMCP) from the bundled MSI.</summary>
[VisualStudioContribution]
public sealed class InstallMcpServiceCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Install TokenSaver MCP Service");

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken) =>
        ServiceInstall.InstallAsync(this.Extensibility, "TokenSaver MCP", ServerControl.ServiceName, ServiceInstall.McpInstallerFile, cancellationToken);
}

/// <summary>Uninstalls the TokenSaver MCP Windows service (TokenSaverMCP) via the bundled MSI.</summary>
[VisualStudioContribution]
public sealed class UninstallMcpServiceCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Uninstall TokenSaver MCP Service");

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken) =>
        ServiceInstall.UninstallAsync(this.Extensibility, "TokenSaver MCP", ServerControl.ServiceName, ServiceInstall.McpInstallerFile, cancellationToken);
}

/// <summary>Installs the SimpleModelServer Windows service from the bundled MSI.</summary>
[VisualStudioContribution]
public sealed class InstallSimpleModelServerCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Install SimpleModelServer Service");

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken) =>
        ServiceInstall.InstallAsync(this.Extensibility, "SimpleModelServer", ServiceInstall.SimpleModelServerServiceName, ServiceInstall.SimpleModelServerInstallerFile, cancellationToken);
}

/// <summary>Uninstalls the SimpleModelServer Windows service via the bundled MSI.</summary>
[VisualStudioContribution]
public sealed class UninstallSimpleModelServerCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Uninstall SimpleModelServer Service");

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken) =>
        ServiceInstall.UninstallAsync(this.Extensibility, "SimpleModelServer", ServiceInstall.SimpleModelServerServiceName, ServiceInstall.SimpleModelServerInstallerFile, cancellationToken);
}
