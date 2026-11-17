using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

using TokenSaver.Vs.Extension.Configuration;
using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.WindowsService;

namespace TokenSaver.Vs.Extension.Services;

/// <summary>Starts SimpleModelServer: its Windows service when installed, otherwise the program at the configured path.</summary>
internal static class SimpleModelServerControl
{
    public static async Task<string> StartAsync()
    {
        var name = ServiceInstall.SimpleModelServerServiceName;
        ExtensionLog.Logger.LogInfo("Start SimpleModelServer requested");

        var state = await WindowsServiceHelper.GetStatusAsync(name).ConfigureAwait(false);
        if (state is not null)
        {
            if (state == ServiceControllerStatus.Running)
            {
                return "SimpleModelServer is already running.";
            }

            await WindowsServiceHelper.StartServiceAsync(name, ExtensionSettings.SimpleModelServerStartTimeout).ConfigureAwait(false);
            return "SimpleModelServer service started.";
        }

        // No service: run the program directly, as the VS Code extension does in its process mode.
        if (await FullSetup.IsSimpleModelServerHealthyAsync(CancellationToken.None).ConfigureAwait(false))
        {
            return "SimpleModelServer is already responding.";
        }

        var exe = ExtensionSettings.SimpleModelServerExePath;
        if (!File.Exists(exe))
        {
            return $"SimpleModelServer is not installed and its program was not found at {exe}. Install the service, or set the program path on the Settings tab.";
        }

        ExtensionLog.Logger.LogInfo($"Launching SimpleModelServer: {exe}");
        using var process = Process.Start(new ProcessStartInfo(exe, "--service")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        return process is null ? $"Could not start {exe}." : $"Started SimpleModelServer from {exe}.";
    }
}
