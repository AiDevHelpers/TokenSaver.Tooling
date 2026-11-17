using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility.UI;

using TokenSaver.Vs.Extension.Configuration;

namespace TokenSaver.Vs.Extension.Settings;

/// <summary>
/// Data context of the settings tool window (see <see cref="SettingsToolWindow"/>). Edits two stores: the
/// extension's own global behavior config (<see cref="BehaviorConfigManager"/>) and the MCP server's settings file
/// in %ProgramData% (<see cref="McpSettingsFile"/>). The MCP server reads its file at startup, so a restart applies
/// those changes. Numeric fields are bound as text and validated on save.
/// </summary>
[DataContract]
internal sealed class SettingsViewModel : NotifyPropertyChangedObject
{
    private const string EndpointSeparator = "|";

    private bool _autostart;
    private bool _verboseLogging;
    private bool _autoWorkflow;
    private string _port = "";
    private string _startTimeout = "";
    private string _idleTimeout = "";
    private string _simpleModelServerExePath = "";
    private string _simpleModelServerHealthUrl = "";
    private string _simpleModelServerStartTimeout = "";
    private bool _mcpAutoRegisterProviders;
    private bool _ideTestRunDotnet;
    private string _mcpOllamaUrl = "";
    private string _mcpOllamaModel = "";
    private string _mcpServerPort = "";
    private string _mcpContextSizeLimit = "";
    private string _mcpHealthCheckInterval = "";
    private string _mcpWorkspaceRoot = "";
    private string _mcpBehaviorConfigPath = "";
    private bool _mcpVerboseLogging;
    private bool _mcpAutoIntercept;
    private bool _mcpAutoWarm;
    private bool _mcpEnableDistill;
    private bool _mcpEnableTerse;
    private bool _mcpEnableSavings;
    private string _mcpCtxSizeLimit = "";
    private string _mcpEndpointStrategy = "Failover";
    private string _mcpEndpoints = "";
    private string _status = "";

    public SettingsViewModel()
    {
        SaveCommand = new AsyncCommand((_, _) =>
        {
            Save();
            return Task.CompletedTask;
        });
        ReloadCommand = new AsyncCommand((_, _) =>
        {
            Load();
            return Task.CompletedTask;
        });
        Load();
    }

    [DataMember] public AsyncCommand SaveCommand { get; }

    [DataMember] public AsyncCommand ReloadCommand { get; }

    // Names of TokenSaver.Common.Core EndpointSelectStrategy. The extension doesn't reference that assembly.
    [DataMember] public IReadOnlyList<string> EndpointStrategies { get; } = ["Failover", "RoundRobin", "Explicit"];

    [DataMember] public bool Autostart { get => _autostart; set => SetProperty(ref _autostart, value); }

    [DataMember] public bool VerboseLogging { get => _verboseLogging; set => SetProperty(ref _verboseLogging, value); }

    [DataMember] public bool AutoWorkflow { get => _autoWorkflow; set => SetProperty(ref _autoWorkflow, value); }

    [DataMember] public string Port { get => _port; set => SetProperty(ref _port, value); }

    [DataMember] public string StartTimeout { get => _startTimeout; set => SetProperty(ref _startTimeout, value); }

    [DataMember] public string IdleTimeout { get => _idleTimeout; set => SetProperty(ref _idleTimeout, value); }

    [DataMember] public string SimpleModelServerExePath { get => _simpleModelServerExePath; set => SetProperty(ref _simpleModelServerExePath, value); }

    [DataMember] public string SimpleModelServerHealthUrl { get => _simpleModelServerHealthUrl; set => SetProperty(ref _simpleModelServerHealthUrl, value); }

    [DataMember] public string SimpleModelServerStartTimeout { get => _simpleModelServerStartTimeout; set => SetProperty(ref _simpleModelServerStartTimeout, value); }

    [DataMember] public bool McpAutoRegisterProviders { get => _mcpAutoRegisterProviders; set => SetProperty(ref _mcpAutoRegisterProviders, value); }

    [DataMember] public bool IdeTestRunDotnet { get => _ideTestRunDotnet; set => SetProperty(ref _ideTestRunDotnet, value); }

    [DataMember] public string McpOllamaUrl { get => _mcpOllamaUrl; set => SetProperty(ref _mcpOllamaUrl, value); }

    [DataMember] public string McpOllamaModel { get => _mcpOllamaModel; set => SetProperty(ref _mcpOllamaModel, value); }

    [DataMember] public string McpServerPort { get => _mcpServerPort; set => SetProperty(ref _mcpServerPort, value); }

    [DataMember] public string McpContextSizeLimit { get => _mcpContextSizeLimit; set => SetProperty(ref _mcpContextSizeLimit, value); }

    [DataMember] public string McpHealthCheckInterval { get => _mcpHealthCheckInterval; set => SetProperty(ref _mcpHealthCheckInterval, value); }

    [DataMember] public string McpWorkspaceRoot { get => _mcpWorkspaceRoot; set => SetProperty(ref _mcpWorkspaceRoot, value); }

    [DataMember] public string McpBehaviorConfigPath { get => _mcpBehaviorConfigPath; set => SetProperty(ref _mcpBehaviorConfigPath, value); }

    [DataMember] public bool McpVerboseLogging { get => _mcpVerboseLogging; set => SetProperty(ref _mcpVerboseLogging, value); }

    [DataMember] public bool McpAutoIntercept { get => _mcpAutoIntercept; set => SetProperty(ref _mcpAutoIntercept, value); }

    [DataMember] public bool McpAutoWarm { get => _mcpAutoWarm; set => SetProperty(ref _mcpAutoWarm, value); }

    [DataMember] public bool McpEnableDistill { get => _mcpEnableDistill; set => SetProperty(ref _mcpEnableDistill, value); }

    [DataMember] public bool McpEnableTerse { get => _mcpEnableTerse; set => SetProperty(ref _mcpEnableTerse, value); }

    [DataMember] public bool McpEnableSavings { get => _mcpEnableSavings; set => SetProperty(ref _mcpEnableSavings, value); }

    [DataMember] public string McpCtxSizeLimit { get => _mcpCtxSizeLimit; set => SetProperty(ref _mcpCtxSizeLimit, value); }

    [DataMember] public string McpEndpointStrategy { get => _mcpEndpointStrategy; set => SetProperty(ref _mcpEndpointStrategy, value); }

    [DataMember] public string McpEndpoints { get => _mcpEndpoints; set => SetProperty(ref _mcpEndpoints, value); }

    [DataMember] public string Status { get => _status; set => SetProperty(ref _status, value); }

    private void Load()
    {
        var settings = BehaviorConfigManager.GetExtensionSettings();
        Autostart = settings.Autostart ?? true;
        VerboseLogging = settings.VerboseLogging ?? false;
        AutoWorkflow = settings.AutoWorkflow ?? false;
        Port = Text(settings.Server?.Port ?? 11436);
        StartTimeout = Text(settings.Server?.StartTimeout ?? 30);
        IdleTimeout = Text(settings.Server?.IdleTimeout ?? 0);
        SimpleModelServerExePath = settings.SimpleModelServer?.ExePath ?? "";
        SimpleModelServerHealthUrl = settings.SimpleModelServer?.HealthUrl?.ToString() ?? "http://localhost:11435/health";
        SimpleModelServerStartTimeout = Text(settings.SimpleModelServer?.StartTimeout ?? 600);
        Status = "";

        try
        {
            LoadMcp(McpSettingsFile.Load());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            Status = $"Could not read {McpSettingsFile.Path}: {ex.Message}";
        }
    }

    private void LoadMcp(JsonObject root)
    {
        McpAutoRegisterProviders = McpSettingsFile.Get(root, "AutoRegisterProviders")?.GetValue<bool>() ?? true;
        IdeTestRunDotnet = McpSettingsFile.Get(root, "IdeTestRunMode")?.GetValue<string>() == "DotnetTest";
        McpOllamaUrl = McpSettingsFile.Get(root, "OllamaUrl")?.GetValue<string>() ?? "http://localhost:11435";
        McpOllamaModel = McpSettingsFile.Get(root, "OllamaModel")?.GetValue<string>() ?? "HelperModel";
        McpServerPort = Text(McpSettingsFile.Get(root, "ServerPort")?.GetValue<int>() ?? 11436);
        McpContextSizeLimit = Text(McpSettingsFile.Get(root, "ContextSizeLimit")?.GetValue<int>() ?? 120_000);
        McpHealthCheckInterval = McpSettingsFile.Get(root, "HealthCheckInterval")?.GetValue<string>() ?? "00:00:30";
        McpWorkspaceRoot = McpSettingsFile.Get(root, "WorkspaceRoot")?.GetValue<string>() ?? "";
        McpBehaviorConfigPath = McpSettingsFile.Get(root, "BehaviorConfigPath")?.GetValue<string>() ?? "";
        LoadMcpAdvanced(root);
    }

    private void LoadMcpAdvanced(JsonObject root)
    {
        McpVerboseLogging = McpSettingsFile.Get(root, "VerboseLogging")?.GetValue<bool>() ?? false;
        McpAutoIntercept = McpSettingsFile.GetPath(root, "ContextHelper:AutoIntercept")?.GetValue<bool>() ?? true;
        McpAutoWarm = McpSettingsFile.GetPath(root, "ContextHelper:AutoWarmOnStartup")?.GetValue<bool>() ?? true;
        McpEnableDistill = McpSettingsFile.GetPath(root, "TokenEconomy:EnableDistill")?.GetValue<bool>() ?? true;
        McpEnableTerse = McpSettingsFile.GetPath(root, "TokenEconomy:EnableTerse")?.GetValue<bool>() ?? true;
        McpEnableSavings = McpSettingsFile.GetPath(root, "TokenEconomy:EnableSavingsTracking")?.GetValue<bool>() ?? true;
        McpCtxSizeLimit = Text(McpSettingsFile.GetPath(root, "ContextHelper:ContextSizeLimit")?.GetValue<int>() ?? 120_000);
        McpEndpointStrategy = McpSettingsFile.Get(root, "EndpointSelectStrategy")?.GetValue<string>() ?? "Failover";
        McpEndpoints = FormatEndpoints(McpSettingsFile.Get(root, "Endpoints") as JsonArray);
    }

    private void Save()
    {
        if (!TryParseInt(Port, out var port) ||
            !TryParseInt(StartTimeout, out var startTimeout) ||
            !TryParseInt(IdleTimeout, out var idleTimeout) ||
            !TryParseInt(SimpleModelServerStartTimeout, out var simpleModelServerStartTimeout))
        {
            Status = "Not saved: timeouts and the port must be whole numbers.";
            return;
        }

        // Validate the MCP section before writing anything, so a bad value never leaves the two stores half saved.
        JsonObject root;
        try
        {
            root = McpSettingsFile.Load();
        }
        catch (JsonException ex)
        {
            // Never overwrite a file we could not read.
            Status = $"Not saved: {McpSettingsFile.Path} is not valid JSON ({ex.Message}).";
            return;
        }

        if (!TryApplyMcp(root, out var error))
        {
            Status = $"Not saved: {error}";
            return;
        }

        try
        {
            BehaviorConfigManager.SaveExtensionSettings(new ExtensionBehaviorConfig
            {
                Autostart = Autostart,
                VerboseLogging = VerboseLogging,
                AutoWorkflow = AutoWorkflow,
                Server = new ExtensionServerConfig
                {
                    Port = port,
                    StartTimeout = startTimeout,
                    IdleTimeout = idleTimeout,
                },
                SimpleModelServer = new ExtensionSimpleModelServerConfig
                {
                    ExePath = SimpleModelServerExePath.Trim(),
                    HealthUrl = Uri.TryCreate(SimpleModelServerHealthUrl.Trim(), UriKind.Absolute, out var healthUri) ? healthUri : null,
                    StartTimeout = simpleModelServerStartTimeout,
                },
            });
            McpSettingsFile.Save(root);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Status = $"Not saved: {ex.Message}";
            return;
        }

        Status = "Saved. Restart the MCP server to apply its settings.";
    }

    private bool TryApplyMcp(JsonObject root, out string? error)
    {
        if (!TryParseInt(McpServerPort, out var port) || !TryParseInt(McpContextSizeLimit, out var contextSize))
        {
            error = "MCP server port and context size must be whole numbers.";
            return false;
        }

        if (!TimeSpan.TryParse(McpHealthCheckInterval.Trim(), CultureInfo.InvariantCulture, out var healthCheck))
        {
            error = "Health check must be a time span, e.g. 00:00:30.";
            return false;
        }

        if (!TryParseInt(McpCtxSizeLimit, out var ctxLimit))
        {
            error = "Context size limit must be a whole number.";
            return false;
        }

        if (!TryParseEndpoints(McpEndpoints, out var endpoints, out error))
        {
            return false;
        }

        McpSettingsFile.Set(root, "AutoRegisterProviders", JsonValue.Create(McpAutoRegisterProviders));
        McpSettingsFile.Set(root, "IdeTestRunMode", JsonValue.Create(IdeTestRunDotnet ? "DotnetTest" : "TestExplorer"));
        McpSettingsFile.Set(root, "OllamaUrl", JsonValue.Create(McpOllamaUrl.Trim()));
        McpSettingsFile.Set(root, "OllamaModel", JsonValue.Create(McpOllamaModel.Trim()));
        McpSettingsFile.Set(root, "ServerPort", JsonValue.Create(port));
        McpSettingsFile.Set(root, "ContextSizeLimit", JsonValue.Create(contextSize));
        McpSettingsFile.Set(root, "HealthCheckInterval", JsonValue.Create(healthCheck.ToString("c", CultureInfo.InvariantCulture)));
        McpSettingsFile.Set(root, "WorkspaceRoot", JsonValue.Create(McpWorkspaceRoot.Trim()));
        McpSettingsFile.Set(root, "BehaviorConfigPath", JsonValue.Create(McpBehaviorConfigPath.Trim()));

        McpSettingsFile.Set(root, "VerboseLogging", JsonValue.Create(McpVerboseLogging));
        McpSettingsFile.SetPath(root, "ContextHelper:AutoIntercept", JsonValue.Create(McpAutoIntercept));
        McpSettingsFile.SetPath(root, "ContextHelper:AutoWarmOnStartup", JsonValue.Create(McpAutoWarm));
        McpSettingsFile.SetPath(root, "ContextHelper:ContextSizeLimit", JsonValue.Create(ctxLimit));
        McpSettingsFile.SetPath(root, "TokenEconomy:EnableDistill", JsonValue.Create(McpEnableDistill));
        McpSettingsFile.SetPath(root, "TokenEconomy:EnableTerse", JsonValue.Create(McpEnableTerse));
        McpSettingsFile.SetPath(root, "TokenEconomy:EnableSavingsTracking", JsonValue.Create(McpEnableSavings));
        McpSettingsFile.Set(root, "EndpointSelectStrategy",
            JsonValue.Create(string.IsNullOrWhiteSpace(McpEndpointStrategy) ? "Failover" : McpEndpointStrategy));
        McpSettingsFile.Set(root, "Endpoints", endpoints);
        error = null;
        return true;
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool TryParseInt(string? text, out int value) =>
        int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static string FormatEndpoints(JsonArray? endpoints)
    {
        if (endpoints is null)
        {
            return "";
        }

        var lines = new List<string>();
        foreach (var node in endpoints)
        {
            if (node is not JsonObject item)
            {
                continue;
            }

            var models = (item["Models"] as JsonArray)?.Select(m => m?.GetValue<string>() ?? "") ?? [];
            lines.Add(string.Join(EndpointSeparator,
                item["Name"]?.GetValue<string>() ?? "",
                item["Url"]?.GetValue<string>() ?? "",
                (item["Primary"]?.GetValue<bool>() ?? false).ToString(CultureInfo.InvariantCulture),
                string.Join(",", models)));
        }

        return string.Join(Environment.NewLine, lines);
    }

    // One endpoint per line: name|url|primary(true|false)|model1,model2. Blank lines are skipped.
    private static bool TryParseEndpoints(string? text, out JsonArray endpoints, out string? error)
    {
        endpoints = [];
        var primaryCount = 0;
        var lineNumber = 0;
        foreach (var raw in (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(EndpointSeparator);
            if (parts.Length != 4 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
            {
                error = $"Endpoint line {lineNumber} must be name|url|primary|models.";
                return false;
            }

            if (!bool.TryParse(parts[2].Trim(), out var primary))
            {
                error = $"Endpoint line {lineNumber}: primary must be true or false.";
                return false;
            }

            if (primary)
            {
                primaryCount++;
            }

            var models = new JsonArray();
            foreach (var model in parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                models.Add(model);
            }

            endpoints.Add(new JsonObject
            {
                ["Name"] = parts[0].Trim(),
                ["Url"] = parts[1].Trim(),
                ["Primary"] = primary,
                ["Models"] = models,
            });
        }

        // OllamaEndpointConfig docs: when endpoints are listed, at least one must have Primary set to true.
        if (endpoints.Count > 0 && primaryCount == 0)
        {
            error = "At least one endpoint line must have primary set to true.";
            return false;
        }

        error = null;
        return true;
    }
}
