using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

using TokenSaver.Vs.Extension.Configuration;
using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.WindowsService;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>
/// One-shot setup: installs SimpleModelServer, waits for it to report healthy (first run can take minutes while
/// models warm up; restarts it once a third of the way in), installs TokenSaver MCP, then waits for it to respond.
/// Each step checks actual state, so re-running skips what is already done. Mirrors the VS Code runFullSetup.
/// </summary>
[VisualStudioContribution]
public sealed class FullSetupCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    private const string FallbackHealthUrl = "http://localhost:11435/health";
    private const int McpPort = 11436;
    private static readonly TimeSpan SimpleModelServerTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan McpTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public override CommandConfiguration CommandConfiguration => new("%FullSetup.DisplayName%");

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        ExtensionLog.Logger.LogInfo("Full setup requested");

        // Step 1: SimpleModelServer service.
        if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServiceInstall.SimpleModelServerServiceName).ConfigureAwait(false))
        {
            var (_, message) = await ServiceInstall.InstallAsync("SimpleModelServer", ServiceInstall.SimpleModelServerServiceName, ServiceInstall.SimpleModelServerInstallerFile).ConfigureAwait(false);
            if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServiceInstall.SimpleModelServerServiceName).ConfigureAwait(false))
            {
                await FailAsync($"TokenSaver setup failed: {message}", cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        // Step 2: wait for SimpleModelServer to be healthy, not just installed. Restart it once after a third of the wait.
        var healthUrl = SimpleModelServerHealthUrl();
        var firstWait = TimeSpan.FromTicks(SimpleModelServerTimeout.Ticks / 3);
        var healthy = await WaitForHealthyAsync(healthUrl, firstWait, cancellationToken).ConfigureAwait(false);
        if (!healthy)
        {
            ExtensionLog.Logger.LogInfo("Full setup: SimpleModelServer not healthy yet; restarting once");
            await WindowsServiceHelper.StopServiceAsync(ServiceInstall.SimpleModelServerServiceName).ConfigureAwait(false);
            await WindowsServiceHelper.StartServiceAsync(ServiceInstall.SimpleModelServerServiceName).ConfigureAwait(false);
            healthy = await WaitForHealthyAsync(healthUrl, SimpleModelServerTimeout - firstWait, cancellationToken).ConfigureAwait(false);
        }

        if (!healthy)
        {
            await FailAsync($"TokenSaver setup failed: SimpleModelServer did not become healthy within {SimpleModelServerTimeout.TotalMinutes:0} minutes.", cancellationToken).ConfigureAwait(false);
            return;
        }

        // Step 3: TokenSaver MCP service.
        if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServerControl.ServiceName).ConfigureAwait(false))
        {
            var (_, message) = await ServiceInstall.InstallAsync("TokenSaver MCP", ServerControl.ServiceName, ServiceInstall.McpInstallerFile).ConfigureAwait(false);
            if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServerControl.ServiceName).ConfigureAwait(false))
            {
                await FailAsync($"TokenSaver setup failed: {message}", cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        // Step 4: wait for the MCP server to respond.
        if (!await WaitForMcpAsync(cancellationToken).ConfigureAwait(false))
        {
            await FailAsync($"TokenSaver setup failed: TokenSaver MCP did not respond on port {McpPort} within {McpTimeout.TotalSeconds:0} seconds.", cancellationToken).ConfigureAwait(false);
            return;
        }

        // Step 5: open the install prompt now that both services are confirmed up.
        var promptError = InstallPromptCommand.Open();
        ExtensionLog.Logger.LogInfo("Full setup complete");
        var doneMessage = promptError is null
            ? "TokenSaver setup complete: SimpleModelServer and TokenSaver MCP are installed and running."
            : $"TokenSaver setup complete, but the install prompt did not open: {promptError}";
        await ServerControl.ShowAsync(this.Extensibility, doneMessage, cancellationToken).ConfigureAwait(false);
    }

    private Task FailAsync(string message, CancellationToken ct)
    {
        ExtensionLog.Logger.LogInfo(message);
        return ServerControl.ShowAsync(this.Extensibility, message, ct);
    }

    private static string SimpleModelServerHealthUrl()
    {
        var url = BehaviorConfigManager.GetExtensionSettings().SimpleModelServer?.HealthUrl;
        return url is null ? FallbackHealthUrl : url.ToString();
    }

    private static async Task<bool> WaitForHealthyAsync(string url, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsOkAsync(url, ct).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<bool> WaitForMcpAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + McpTimeout;
        using var extension = new TokenSaverExtension(Environment.CurrentDirectory, ExtensionLog.Logger);
        while (DateTime.UtcNow < deadline)
        {
            if (await extension.CheckServerHealthAsync(McpPort).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<bool> IsOkAsync(string url, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await http.GetAsync(new Uri(url), ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Per-request timeout, not cancellation of the whole setup.
            return false;
        }
    }
}
