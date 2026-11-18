using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using EnvDTE;
using EnvDTE80;

using Microsoft.VisualStudio.Shell;

using SperryDev.Ide.Bridge;
using TokenSaver.Vs.Extension.Bridge;
using TokenSaver.Vs.Extension.Logging;

namespace TokenSaver.Vs.Extension.Options;

/// <summary>
/// Classic (VSSDK) package providing the Tools &gt; Options &gt; TokenSaver &gt; General page. Only
/// exists because <see cref="ProvideOptionPageAttribute"/>/<see cref="DialogPage"/> have no
/// equivalent in the out-of-process VisualStudio.Extensibility SDK — see
/// <see cref="ExtensionEntrypoint"/> and TokenSaver.Vs.Extension.csproj's top comment for why this
/// extension now hosts in-process (VssdkCompatibleExtension/RequiresInProcessHosting) instead of
/// out-of-process.
///
/// Also starts the per-instance named-pipe bridge and keeps this instance's manifest in sync with
/// the open solution, so MCP can tell which Visual Studio instance has which solution open.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
public sealed class TokenSaverPackage : AsyncPackage, IDisposable
{
    public const string PackageGuidString = "34c83c12-40e0-4c22-bf7e-f6f2f2a16636";

    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(10);

    private BridgeServer? _bridge;
    private SolutionEvents? _solutionEvents;
    private volatile string? _solutionPath;
    private System.Threading.Timer? _watchdog;

    /// <summary>Stops the bridge and the watchdog timer.</summary>
    public void Dispose()
    {
        _bridge?.Dispose();
        _watchdog?.Dispose();
    }

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        var logger = ExtensionLog.Logger;
        logger.LogInfo("TokenSaver package initialized");

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dte = (DTE2?)await GetServiceAsync(typeof(DTE)) ?? throw new InvalidOperationException("Visual Studio DTE service is unavailable.");

        var pid = Environment.ProcessId;
        _solutionPath = NullIfEmpty(dte.Solution?.FullName);

        var actions = new IdeActions(dte);
        var pipeName = InstanceRegistry.PipeNameFor(pid);
        _bridge = new BridgeServer(
            pipeName,
            async (action, args) =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                return actions.Invoke(action, args);
            },
            () => _solutionPath,
            new BridgeLogger(logger));
        _bridge.Start();
        BridgeControl.Attach(_bridge, pid, pipeName, () => _solutionPath);

        InstanceRegistry.Write(pid, _solutionPath);

        // Watchdog: restarts the bridge if its listen loop stopped, and rewrites the manifest so MCP can
        // still find this instance after the file was removed or the solution changed.
        _watchdog = new System.Threading.Timer(_ => Watchdog(pid), null, WatchdogInterval, WatchdogInterval);

        _solutionEvents = dte.Events.SolutionEvents;
#pragma warning disable VSTHRD010 // DTE solution events are raised on the UI thread.
        _solutionEvents.Opened += () => UpdateSolutionPath(dte.Solution?.FullName);
        _solutionEvents.AfterClosing += () => UpdateSolutionPath(null);
#pragma warning restore VSTHRD010

        AppDomain.CurrentDomain.ProcessExit += (_, _) => InstanceRegistry.Remove(pid);
    }

    private void Watchdog(int pid)
    {
        try
        {
            // A bridge the user stopped stays stopped, and its registry entry stays removed, until Start is pressed.
            if (BridgeControl.UserStopped)
            {
                return;
            }

            if (_bridge is { IsRunning: false })
            {
                ExtensionLog.Logger.LogError("TokenSaver bridge stopped; restarting it.");
                _bridge.Start();
            }

            InstanceRegistry.Write(pid, _solutionPath);
        }
        catch (Exception ex)
        {
            // Never let the timer die: the next tick retries.
            ExtensionLog.Logger.LogException(ex);
        }
    }

    private void UpdateSolutionPath(string? solutionPath)
    {
        _solutionPath = NullIfEmpty(solutionPath);
        InstanceRegistry.Write(Environment.ProcessId, _solutionPath);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
