using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility.UI;

using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.Services;
using TokenSaver.Vs.Extension.WindowsService;

namespace TokenSaver.Vs.Extension.Pages;

/// <summary>
/// Data context of the Services tab of <see cref="TokenSaverToolWindow"/>: state of the two Windows services, and
/// the actions that start, stop, install and uninstall them. One action runs at a time; its result goes to
/// <see cref="Message"/> and the states are read again afterwards.
/// </summary>
[DataContract]
internal sealed class ServicesViewModel : NotifyPropertyChangedObject
{
    private string _mcpStatus = "";
    private string _simpleModelServerStatus = "";
    private string _message = "";
    private int _busy;

    public ServicesViewModel()
    {
        RefreshCommand = Action("Checking services...", () => Task.FromResult(""));
        StartMcpCommand = Action("Starting TokenSaver MCP...", ServerControl.StartAsync);
        StopMcpCommand = Action("Stopping TokenSaver MCP...", ServerControl.StopAsync);
        RestartMcpCommand = Action("Restarting TokenSaver MCP...", ServerControl.RestartAsync);
        InstallMcpCommand = Action("Installing TokenSaver MCP...", async () =>
            (await ServiceInstall.InstallAsync(ServiceInstall.McpLabel, ServerControl.ServiceName, ServiceInstall.McpInstallerFile).ConfigureAwait(false)).message);
        UninstallMcpCommand = Action("Uninstalling TokenSaver MCP...", async () =>
            (await ServiceInstall.UninstallAsync(ServiceInstall.McpLabel, ServerControl.ServiceName, ServiceInstall.McpInstallerFile).ConfigureAwait(false)).message);
        InstallSimpleModelServerCommand = Action("Installing SimpleModelServer...", async () =>
            (await ServiceInstall.InstallAsync(ServiceInstall.SimpleModelServerLabel, ServiceInstall.SimpleModelServerServiceName, ServiceInstall.SimpleModelServerInstallerFile).ConfigureAwait(false)).message);
        UninstallSimpleModelServerCommand = Action("Uninstalling SimpleModelServer...", async () =>
            (await ServiceInstall.UninstallAsync(ServiceInstall.SimpleModelServerLabel, ServiceInstall.SimpleModelServerServiceName, ServiceInstall.SimpleModelServerInstallerFile).ConfigureAwait(false)).message);
        FullSetupCommand = Action("Running full setup...", () => FullSetup.RunAsync(step => Message = step, CancellationToken.None));
        InstallPromptCommand = Action("Opening the install prompt...", () => Task.FromResult(InstallPrompt.Open() ?? "Install prompt opened."));
    }

    [DataMember] public AsyncCommand RefreshCommand { get; }

    [DataMember] public AsyncCommand StartMcpCommand { get; }

    [DataMember] public AsyncCommand StopMcpCommand { get; }

    [DataMember] public AsyncCommand RestartMcpCommand { get; }

    [DataMember] public AsyncCommand InstallMcpCommand { get; }

    [DataMember] public AsyncCommand UninstallMcpCommand { get; }

    [DataMember] public AsyncCommand InstallSimpleModelServerCommand { get; }

    [DataMember] public AsyncCommand UninstallSimpleModelServerCommand { get; }

    [DataMember] public AsyncCommand FullSetupCommand { get; }

    [DataMember] public AsyncCommand InstallPromptCommand { get; }

    [DataMember] public string McpStatus { get => _mcpStatus; set => SetProperty(ref _mcpStatus, value); }

    [DataMember] public string SimpleModelServerStatus { get => _simpleModelServerStatus; set => SetProperty(ref _simpleModelServerStatus, value); }

    [DataMember] public string Message { get => _message; set => SetProperty(ref _message, value); }

    /// <summary>Reads both service states again, unless an action is running (it refreshes when it finishes).</summary>
    public Task RefreshAsync() => RunAsync(null, () => Task.FromResult(Message));

    private AsyncCommand Action(string working, Func<Task<string>> operation) =>
        new((_, _) => RunAsync(working, operation));

    private async Task RunAsync(string? working, Func<Task<string>> operation)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            if (working is not null)
            {
                Message = working;
            }

            Message = await operation().ConfigureAwait(false);
            await ReadStatusAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failed service call must reach the page as a message, not fault the Remote UI command.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ExtensionLog.Logger.LogException(ex);
            Message = $"Failed: {ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task ReadStatusAsync()
    {
        McpStatus = await ServerControl.StatusAsync().ConfigureAwait(false);

        if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServiceInstall.SimpleModelServerServiceName).ConfigureAwait(false))
        {
            SimpleModelServerStatus = "Not installed";
            return;
        }

        SimpleModelServerStatus = await FullSetup.IsSimpleModelServerHealthyAsync(CancellationToken.None).ConfigureAwait(false)
            ? "Installed, healthy"
            : "Installed, not healthy";
    }
}
