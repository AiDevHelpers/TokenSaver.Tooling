using System.IO;
using System.Threading.Tasks;

using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.WindowsService;

namespace TokenSaver.Vs.Extension.Services;

/// <summary>Shared install/uninstall flow for the two bundled service installers. Returns a message, shows nothing.</summary>
internal static class ServiceInstall
{
    public const string McpLabel = "TokenSaver MCP";
    public const string SimpleModelServerLabel = "SimpleModelServer";
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
