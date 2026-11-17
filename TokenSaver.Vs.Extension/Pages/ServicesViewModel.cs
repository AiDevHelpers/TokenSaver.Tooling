using System;
using System.Runtime.Serialization;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility.UI;

using TokenSaver.Vs.Extension.Bridge;
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
internal sealed class ServicesViewModel : NotifyPropertyChangedObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private string _mcpStatus = "Checking...";
    private string _simpleModelServerStatus = "Checking...";
    private string _bridgeStatus = "Checking...";
    private string _message = "";
    private int _busy;
    private Timer? _timer;

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
        StartSimpleModelServerCommand = Action("Starting SimpleModelServer...", SimpleModelServerControl.StartAsync);
        InstallSimpleModelServerCommand = Action("Installing SimpleModelServer...", async () =>
            (await ServiceInstall.InstallAsync(ServiceInstall.SimpleModelServerLabel, ServiceInstall.SimpleModelServerServiceName, ServiceInstall.SimpleModelServerInstallerFile).ConfigureAwait(false)).message);
        UninstallSimpleModelServerCommand = Action("Uninstalling SimpleModelServer...", async () =>
            (await ServiceInstall.UninstallAsync(ServiceInstall.SimpleModelServerLabel, ServiceInstall.SimpleModelServerServiceName, ServiceInstall.SimpleModelServerInstallerFile).ConfigureAwait(false)).message);
        FullSetupCommand = Action("Running full setup...", () => FullSetup.RunAsync(step => Message = step, CancellationToken.None));
        InstallPromptCommand = Action("Opening the install prompt...", () => Task.FromResult(InstallPrompt.Open() ?? "Install prompt opened."));
        StartBridgeCommand = Action("Starting the IDE bridge...", () => Task.FromResult(BridgeControl.Start()));
        StopBridgeCommand = Action("Stopping the IDE bridge...", () => Task.FromResult(BridgeControl.Stop()));
    }

    [DataMember] public AsyncCommand RefreshCommand { get; }

    [DataMember] public AsyncCommand StartMcpCommand { get; }

    [DataMember] public AsyncCommand StopMcpCommand { get; }

    [DataMember] public AsyncCommand RestartMcpCommand { get; }

    [DataMember] public AsyncCommand InstallMcpCommand { get; }

    [DataMember] public AsyncCommand UninstallMcpCommand { get; }

    [DataMember] public AsyncCommand StartSimpleModelServerCommand { get; }

    [DataMember] public AsyncCommand InstallSimpleModelServerCommand { get; }

    [DataMember] public AsyncCommand UninstallSimpleModelServerCommand { get; }

    [DataMember] public AsyncCommand FullSetupCommand { get; }

    [DataMember] public AsyncCommand InstallPromptCommand { get; }

    [DataMember] public AsyncCommand StartBridgeCommand { get; }

    [DataMember] public AsyncCommand StopBridgeCommand { get; }

    [DataMember] public string McpStatus { get => _mcpStatus; set => SetProperty(ref _mcpStatus, value); }

    [DataMember] public string SimpleModelServerStatus { get => _simpleModelServerStatus; set => SetProperty(ref _simpleModelServerStatus, value); }

    [DataMember] public string BridgeStatus { get => _bridgeStatus; set => SetProperty(ref _bridgeStatus, value); }

    [DataMember] public string Message { get => _message; set => SetProperty(ref _message, value); }

    /// <summary>Reads both service states again, unless an action is running (it refreshes when it finishes).</summary>
    public Task RefreshAsync() => RunAsync(null, () => Task.FromResult(Message));

    /// <summary>Reads the states now and then every few seconds, so a service stopped outside Visual Studio shows up. Safe to call again.</summary>
    public void Start()
    {
        _timer ??= new Timer(_ => _ = RefreshAsync(), null, TimeSpan.Zero, PollInterval);
    }

    public void Dispose() => _timer?.Dispose();

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

    private Task ReadStatusAsync()
    {
        BridgeStatus = BridgeControl.Describe();
        return Task.WhenAll(
            ReadOneAsync(ServerControl.ServiceName, text => McpStatus = text, () => ServerControl.IsRespondingAsync(), "server responding", "server not responding"),
            ReadOneAsync(ServiceInstall.SimpleModelServerServiceName, text => SimpleModelServerStatus = text, () => FullSetup.IsSimpleModelServerHealthyAsync(CancellationToken.None), "healthy", "not healthy yet"));
    }

    /// <summary>
    /// Publishes one service's state. The service state is shown first and the health answer is added when it
    /// arrives, so a slow or failing health check never leaves the line stuck on "Checking...".
    /// </summary>
    private static async Task ReadOneAsync(string serviceName, Action<string> publish, Func<Task<bool>> probe, string ok, string bad)
    {
#pragma warning disable CA1031 // A failed read must reach the page as text, not stop the other service's line.
        try
        {
            var state = await WindowsServiceHelper.GetStatusAsync(serviceName).ConfigureAwait(false);
            if (state is null)
            {
                publish("Not installed");
                return;
            }

            if (state != ServiceControllerStatus.Running)
            {
                publish(ServerControl.Describe(state.Value));
                return;
            }

            publish("Running");
            publish(await probe().ConfigureAwait(false) ? $"Running, {ok}" : $"Running, {bad}");
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.LogException(ex);
            publish($"Could not read the state: {ex.Message}");
        }
#pragma warning restore CA1031
    }
}
