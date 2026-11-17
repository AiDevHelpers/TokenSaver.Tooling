using System;
using System.Threading;
using System.Threading.Tasks;

using TokenSaver.Vs.Extension.Configuration;
using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.WindowsService;

namespace TokenSaver.Vs.Extension.Services;

/// <summary>
/// Starts SimpleModelServer and then TokenSaver MCP once, the first time the extension loads in a Visual Studio
/// session, when the Autostart setting is on. Visual Studio loads an out-of-process extension on first use (a menu
/// command or the TokenSaver window), not at IDE startup. Each step checks real state, so services that already
/// run are left alone.
/// </summary>
internal static class AutoStart
{
    private static int s_ran;

    public static async Task RunOnceAsync()
    {
        if (Interlocked.Exchange(ref s_ran, 1) == 1)
        {
            return;
        }

#pragma warning disable CA1031 // Runs unobserved in the background: a failure is logged, never thrown into the extension host.
        try
        {
            if (!ExtensionSettings.Autostart)
            {
                ExtensionLog.Logger.LogDebug("Autostart is off.");
                return;
            }

            ExtensionLog.Logger.LogInfo("Autostart: starting SimpleModelServer, then TokenSaver MCP");
            ExtensionLog.Logger.LogInfo($"Autostart: {await SimpleModelServerControl.StartAsync().ConfigureAwait(false)}");

            // TokenSaver MCP calls SimpleModelServer, so give SimpleModelServer its start timeout to answer first.
            if (!await FullSetup.WaitForSimpleModelServerHealthyAsync(CancellationToken.None).ConfigureAwait(false))
            {
                ExtensionLog.Logger.LogInfo("Autostart: SimpleModelServer is not healthy yet; starting TokenSaver MCP anyway.");
            }

            ExtensionLog.Logger.LogInfo($"Autostart: {await ServerControl.StartAsync().ConfigureAwait(false)}");
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.LogException(ex);
        }
#pragma warning restore CA1031
    }
}
