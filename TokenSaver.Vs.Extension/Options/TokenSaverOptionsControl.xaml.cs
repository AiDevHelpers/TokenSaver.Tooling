using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Controls;

using TokenSaver.Vs.Extension.Configuration;

namespace TokenSaver.Vs.Extension.Options;

/// <summary>WPF content of <see cref="TokenSaverOptionsPage"/> — see that type's remarks.</summary>
public partial class TokenSaverOptionsControl : UserControl
{
    public TokenSaverOptionsControl()
    {
        InitializeComponent();
    }

    internal void Load(ExtensionBehaviorConfig settings)
    {
        AutostartCheckBox.IsChecked = settings.Autostart ?? true;
        VerboseLoggingCheckBox.IsChecked = settings.VerboseLogging ?? false;
        AutoWorkflowCheckBox.IsChecked = settings.AutoWorkflow ?? false;
        PortTextBox.Text = (settings.Server?.Port ?? 11436).ToString(CultureInfo.InvariantCulture);
        StartTimeoutTextBox.Text = (settings.Server?.StartTimeout ?? 30).ToString(CultureInfo.InvariantCulture);
        IdleTimeoutTextBox.Text = (settings.Server?.IdleTimeout ?? 0).ToString(CultureInfo.InvariantCulture);
        SimpleModelServerExePathTextBox.Text = settings.SimpleModelServer?.ExePath ?? "";
        SimpleModelServerHealthUrlTextBox.Text = settings.SimpleModelServer?.HealthUrl?.ToString() ?? "http://localhost:11435/health";
        SimpleModelServerStartTimeoutTextBox.Text = (settings.SimpleModelServer?.StartTimeout ?? 600).ToString(CultureInfo.InvariantCulture);
        StatusTextBlock.Text = string.Empty;
    }

    internal bool TryBuildSettings(out ExtensionBehaviorConfig settings, out string? error)
    {
        if (!TryParseInt(PortTextBox.Text, out var port) ||
            !TryParseInt(StartTimeoutTextBox.Text, out var startTimeout) ||
            !TryParseInt(IdleTimeoutTextBox.Text, out var idleTimeout) ||
            !TryParseInt(SimpleModelServerStartTimeoutTextBox.Text, out var simpleModelServerStartTimeout))
        {
            error = "Timeouts and the port must be whole numbers.";
            StatusTextBlock.Text = error;
            settings = new ExtensionBehaviorConfig();
            return false;
        }

        error = null;
        settings = new ExtensionBehaviorConfig
        {
            Autostart = AutostartCheckBox.IsChecked ?? true,
            VerboseLogging = VerboseLoggingCheckBox.IsChecked ?? false,
            AutoWorkflow = AutoWorkflowCheckBox.IsChecked ?? false,
            Server = new ExtensionServerConfig
            {
                Port = port,
                StartTimeout = startTimeout,
                IdleTimeout = idleTimeout,
            },
            SimpleModelServer = new ExtensionSimpleModelServerConfig
            {
                ExePath = SimpleModelServerExePathTextBox.Text.Trim(),
                HealthUrl = Uri.TryCreate(SimpleModelServerHealthUrlTextBox.Text.Trim(), UriKind.Absolute, out var healthUri) ? healthUri : null,
                StartTimeout = simpleModelServerStartTimeout,
            },
        };
        return true;
    }

    internal void SetStatus(string message) => StatusTextBlock.Text = message;

    internal void LoadMcp(JsonObject root)
    {
        McpAutoRegisterProvidersCheckBox.IsChecked = McpSettingsFile.Get(root, "AutoRegisterProviders")?.GetValue<bool>() ?? true;
        IdeTestRunDotnetCheckBox.IsChecked = McpSettingsFile.Get(root, "IdeTestRunMode")?.GetValue<string>() == "DotnetTest";
        McpOllamaUrlTextBox.Text = McpSettingsFile.Get(root, "OllamaUrl")?.GetValue<string>() ?? "http://localhost:11435";
        McpOllamaModelTextBox.Text = McpSettingsFile.Get(root, "OllamaModel")?.GetValue<string>() ?? "HelperModel";
        McpServerPortTextBox.Text = (McpSettingsFile.Get(root, "ServerPort")?.GetValue<int>() ?? 11436).ToString(CultureInfo.InvariantCulture);
        McpContextSizeLimitTextBox.Text = (McpSettingsFile.Get(root, "ContextSizeLimit")?.GetValue<int>() ?? 120_000).ToString(CultureInfo.InvariantCulture);
        McpHealthCheckIntervalTextBox.Text = (McpSettingsFile.Get(root, "HealthCheckInterval")?.GetValue<string>() ?? "00:00:30");
        McpWorkspaceRootTextBox.Text = McpSettingsFile.Get(root, "WorkspaceRoot")?.GetValue<string>() ?? "";
        McpBehaviorConfigPathTextBox.Text = McpSettingsFile.Get(root, "BehaviorConfigPath")?.GetValue<string>() ?? "";
        LoadAdvanced(root);
    }

    /// <summary>Writes the MCP section's edited values into <paramref name="root"/>. Returns false with an error on bad input.</summary>
    internal bool TryApplyMcp(JsonObject root, out string? error)
    {
        if (!TryParseInt(McpServerPortTextBox.Text, out var port) ||
            !TryParseInt(McpContextSizeLimitTextBox.Text, out var contextSize))
        {
            error = "MCP server port and context size must be whole numbers.";
            return false;
        }

        if (!TimeSpan.TryParse(McpHealthCheckIntervalTextBox.Text.Trim(), CultureInfo.InvariantCulture, out var healthCheck))
        {
            error = "Health check must be a time span, e.g. 00:00:30.";
            return false;
        }

        McpSettingsFile.Set(root, "AutoRegisterProviders", JsonValue.Create(McpAutoRegisterProvidersCheckBox.IsChecked ?? true));
        McpSettingsFile.Set(root, "IdeTestRunMode", JsonValue.Create(IdeTestRunDotnetCheckBox.IsChecked == true ? "DotnetTest" : "TestExplorer"));
        McpSettingsFile.Set(root, "OllamaUrl", JsonValue.Create(McpOllamaUrlTextBox.Text.Trim()));
        McpSettingsFile.Set(root, "OllamaModel", JsonValue.Create(McpOllamaModelTextBox.Text.Trim()));
        McpSettingsFile.Set(root, "ServerPort", JsonValue.Create(port));
        McpSettingsFile.Set(root, "ContextSizeLimit", JsonValue.Create(contextSize));
        McpSettingsFile.Set(root, "HealthCheckInterval", JsonValue.Create(healthCheck.ToString("c", CultureInfo.InvariantCulture)));
        McpSettingsFile.Set(root, "WorkspaceRoot", JsonValue.Create(McpWorkspaceRootTextBox.Text.Trim()));
        McpSettingsFile.Set(root, "BehaviorConfigPath", JsonValue.Create(McpBehaviorConfigPathTextBox.Text.Trim()));
        if (!TryApplyAdvanced(root, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
