using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using EnvDTE;
using EnvDTE80;

using Microsoft.VisualStudio.Shell;

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
[ProvideOptionPage(typeof(TokenSaverOptionsPage), "TokenSaver", "General", 0, 0, supportsAutomation: true)]
public sealed class TokenSaverPackage : AsyncPackage
{
    public const string PackageGuidString = "34c83c12-40e0-4c22-bf7e-f6f2f2a16636";

    private BridgeServer? _bridge;
    private SolutionEvents? _solutionEvents;
    private string? _solutionPath;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        var logger = ExtensionLog.Logger;
        logger.LogInfo("TokenSaver package initialized");

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dte = (DTE2)await GetServiceAsync(typeof(DTE));

        var pid = Environment.ProcessId;
        _solutionPath = NullIfEmpty(dte.Solution?.FullName);

        var actions = new IdeActions(dte);
        _bridge = new BridgeServer(
            InstanceRegistry.PipeNameFor(pid),
            async (action, args) =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                return actions.Invoke(action, args);
            },
            logger);
        _bridge.Start();

        InstanceRegistry.Write(pid, _solutionPath);

        _solutionEvents = dte.Events.SolutionEvents;
        _solutionEvents.Opened += () => UpdateSolutionPath(dte.Solution?.FullName);
        _solutionEvents.AfterClosing += () => UpdateSolutionPath(null);

        AppDomain.CurrentDomain.ProcessExit += (_, _) => InstanceRegistry.Remove(pid);
    }

    private void UpdateSolutionPath(string? solutionPath)
    {
        _solutionPath = NullIfEmpty(solutionPath);
        InstanceRegistry.Write(Environment.ProcessId, _solutionPath);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
