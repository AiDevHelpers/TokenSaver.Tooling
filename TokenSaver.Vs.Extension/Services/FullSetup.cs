using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using TokenSaver.Vs.Extension.Configuration;
using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.WindowsService;

namespace TokenSaver.Vs.Extension.Services;

/// <summary>
/// One-shot setup: installs SimpleModelServer, waits for it to report healthy (first run can take minutes while
/// models warm up; restarts it once a third of the way in), installs TokenSaver MCP, then waits for it to respond.
/// Each step checks actual state, so re-running skips what is already done. Mirrors the VS Code runFullSetup.
/// </summary>
internal static class FullSetup
{
    private const string FallbackHealthUrl = "http://localhost:11435/health";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private static TimeSpan SimpleModelServerTimeout => ExtensionSettings.SimpleModelServerStartTimeout;

    private static TimeSpan McpTimeout => ExtensionSettings.McpStartTimeout;

    /// <summary>Runs the setup. <paramref name="progress"/> gets the step in hand; the result is the final message.</summary>
    public static async Task<string> RunAsync(Action<string> progress, CancellationToken cancellationToken)
    {
        ExtensionLog.Logger.LogInfo("Full setup requested");

        // Step 1: SimpleModelServer service.
        progress("Full setup: installing SimpleModelServer...");
        if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServiceInstall.SimpleModelServerServiceName).ConfigureAwait(false))
        {
            var (_, message) = await ServiceInstall.InstallAsync(ServiceInstall.SimpleModelServerLabel, ServiceInstall.SimpleModelServerServiceName, ServiceInstall.SimpleModelServerInstallerFile).ConfigureAwait(false);
            if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServiceInstall.SimpleModelServerServiceName).ConfigureAwait(false))
            {
                return Fail(message);
            }
        }

        // Step 2: wait for SimpleModelServer to be healthy, not just installed. Restart it once after a third of the wait.
        progress($"Full setup: waiting for SimpleModelServer to become healthy (up to {SimpleModelServerTimeout.TotalSeconds:0} seconds)...");
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
            return Fail($"SimpleModelServer did not become healthy within {SimpleModelServerTimeout.TotalSeconds:0} seconds.");
        }

        // Step 3: TokenSaver MCP service.
        progress("Full setup: installing TokenSaver MCP...");
        if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServerControl.ServiceName).ConfigureAwait(false))
        {
            var (_, message) = await ServiceInstall.InstallAsync(ServiceInstall.McpLabel, ServerControl.ServiceName, ServiceInstall.McpInstallerFile).ConfigureAwait(false);
            if (!await WindowsServiceHelper.IsServiceInstalledAsync(ServerControl.ServiceName).ConfigureAwait(false))
            {
                return Fail(message);
            }
        }

        // Step 4: wait for the MCP server to respond.
        progress("Full setup: waiting for TokenSaver MCP to respond...");
        if (!await WaitForMcpAsync(cancellationToken).ConfigureAwait(false))
        {
            return Fail($"TokenSaver MCP did not respond on port {ExtensionSettings.McpPort} within {McpTimeout.TotalSeconds:0} seconds.");
        }

        // Step 5: open the install prompt now that both services are confirmed up.
        var promptError = InstallPrompt.Open();
        ExtensionLog.Logger.LogInfo("Full setup complete");
        return promptError is null
            ? "TokenSaver setup complete: SimpleModelServer and TokenSaver MCP are installed and running."
            : $"TokenSaver setup complete, but the install prompt did not open: {promptError}";
    }

    /// <summary>True when the SimpleModelServer health URL from the settings answers with a success status.</summary>
    public static Task<bool> IsSimpleModelServerHealthyAsync(CancellationToken cancellationToken) =>
        IsOkAsync(SimpleModelServerHealthUrl(), cancellationToken);

    /// <summary>Waits up to the configured SimpleModelServer start timeout for its health URL to answer.</summary>
    public static Task<bool> WaitForSimpleModelServerHealthyAsync(CancellationToken cancellationToken) =>
        WaitForHealthyAsync(SimpleModelServerHealthUrl(), SimpleModelServerTimeout, cancellationToken);

    private static string Fail(string reason)
    {
        var message = $"TokenSaver setup failed: {reason}";
        ExtensionLog.Logger.LogInfo(message);
        return message;
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
        while (DateTime.UtcNow < deadline)
        {
            if (await ServerControl.IsRespondingAsync().ConfigureAwait(false))
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
