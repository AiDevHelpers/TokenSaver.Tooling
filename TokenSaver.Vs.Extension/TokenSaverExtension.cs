using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using TokenSaver.Vs.Extension.Configuration;
using TokenSaver.Vs.Extension.Health;
using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.WindowsService;
using McpHealthReport = TokenSaver.Vs.Extension.Health.McpHealthReport;

namespace TokenSaver.Vs.Extension;

/// <summary>
/// Main extension class for Visual Studio 2026 TokenSaver integration.
/// Manages server lifecycle and provides commands for user interaction.
/// </summary>
public sealed class TokenSaverExtension : IDisposable
{
    private const string TokenSaverServiceName = "TokenSaverMCP";

    // Workspaces already shown the .gitignore notice this session, so it is not repeated on every command.
    private static readonly ConcurrentDictionary<string, bool> PromptedWorkspaces = new(StringComparer.OrdinalIgnoreCase);

    private readonly IExtensionLogger _logger;
    private readonly string _workspaceRoot;
    private readonly Func<string, Task>? _notify;
    private bool _usingTokenSaverService;

    public TokenSaverExtension(string workspaceRoot, IExtensionLogger logger, Func<string, Task>? notify = null)
    {
        _workspaceRoot = workspaceRoot ?? throw new ArgumentNullException(nameof(workspaceRoot));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _notify = notify;
    }

    /// <summary>
    /// Initializes the extension on activation.
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInfo("TokenSaver extension initializing...");

        // Check behavior config and prompt for gitignore if needed
        await MaybePromptAddTokenSaverToGitignoreAsync();

        _logger.LogInfo("TokenSaver extension initialized");
    }

    /// <summary>
    /// Prompts the user to add .tokensaver/ to .gitignore if not already there.
    /// </summary>
    public async Task MaybePromptAddTokenSaverToGitignoreAsync()
    {
        var config = BehaviorConfigManager.GetMergedRepoHelperConfig(_workspaceRoot);

        if (config.AddTokenSaverToGitignore != null)
            return; // User has already answered

        if (GitignoreHelper.IsTokenSaverInGitignore(_workspaceRoot))
            return; // Already covered

        if (!PromptedWorkspaces.TryAdd(_workspaceRoot, true))
            return; // Already shown this session

        _logger.LogInfo("Checking .gitignore configuration...");

        // In a real VS extension, this would show a dialog asking the user.
        // For now, we log it.
        var shouldAdd = await PromptUserAsync(
            "TokenSaver writes its cache to `.tokensaver/`, which isn't in this workspace's .gitignore. Add it?",
            "Yes (this workspace)", "Yes (all workspaces)", "No");

        if (shouldAdd == null)
            return;

        var (add, isGlobal) = shouldAdd.Value;
        var scope = isGlobal ? ConfigScope.Global : ConfigScope.Local;

        BehaviorConfigManager.UpdateConfigValue(_workspaceRoot, scope, cfg =>
        {
            return cfg with
            {
                RepoHelper = (cfg.RepoHelper ?? new()) with
                {
                    AddTokenSaverToGitignore = add,
                }
            };
        });

        if (add)
        {
            GitignoreHelper.AddTokenSaverToGitignore(_workspaceRoot);
            _logger.LogInfo("Added .tokensaver/ to .gitignore");
        }
    }

    /// <summary>
    /// Starts TokenSaver.MCP server.
    /// </summary>
    public async Task StartServerAsync(int? mcpPort = null, TimeSpan? timeout = null)
    {
        var port = mcpPort ?? ExtensionSettings.McpPort;
        timeout ??= ExtensionSettings.McpStartTimeout;
        _logger.LogInfo($"Starting TokenSaver.MCP on port {port}...");

        // Check if service is installed and use it
        if (await WindowsServiceHelper.IsServiceInstalledAsync(TokenSaverServiceName))
        {
            _usingTokenSaverService = true;
            await WindowsServiceHelper.StartServiceAsync(TokenSaverServiceName);
            _logger.LogInfo("TokenSaverMCP service started");

            // Wait for it to be reachable
            if (await HealthCheckClient.WaitForPortOpenAsync("127.0.0.1", port, timeout.Value))
            {
                _logger.LogInfo("TokenSaver.MCP is reachable");
                return;
            }
        }

        _logger.LogWarning("TokenSaver.MCP service not installed or not responding");
    }

    /// <summary>
    /// Stops TokenSaver.MCP server.
    /// </summary>
    public async Task StopServerAsync()
    {
        _logger.LogInfo("Stopping TokenSaver.MCP...");

        if (_usingTokenSaverService)
        {
            await WindowsServiceHelper.StopServiceAsync(TokenSaverServiceName);
            _logger.LogInfo("TokenSaverMCP service stopped");
        }
    }

    /// <summary>
    /// Checks the health of the TokenSaver.MCP server.
    /// </summary>
    public async Task<bool> CheckServerHealthAsync(int? mcpPort = null)
    {
        try
        {
            return await HealthCheckClient.VerifyMcpServerAsync(mcpPort ?? ExtensionSettings.McpPort);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex);
            return false;
        }
    }

    /// <summary>
    /// Gets the full health report from TokenSaver.MCP.
    /// </summary>
    public async Task<McpHealthReport?> GetFullHealthAsync(int? mcpPort = null)
    {
        try
        {
            return await HealthCheckClient.GetFullHealthAsync(mcpPort ?? ExtensionSettings.McpPort);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex);
            return null;
        }
    }

    /// <summary>
    /// Prompts the user with a question (to be implemented by the VS extension UI).
    /// </summary>
    private async Task<(bool add, bool global)?> PromptUserAsync(string message, string option1, string option2, string option3)
    {
        // Yes/No choice is not implemented yet; the notice is shown and no change is made.
        _logger.LogInfo(message);
        if (_notify is not null)
        {
            await _notify(message).ConfigureAwait(false);
        }

        return null;
    }

    public void Dispose()
    {
        // Cleanup
    }
}

/// <summary>
/// Extension to IExtensionLogger for convenience methods.
/// </summary>
public static class ExtensionLoggerExtensions
{
    public static void LogWarning(this IExtensionLogger logger, string message)
    {
        logger.LogError($"WARNING: {message}");
    }
}
