using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility.UI;

using TokenSaver.Vs.Extension.Configuration;
using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.Services;

namespace TokenSaver.Vs.Extension.Pages;

/// <summary>
/// Data context of the Settings tab of <see cref="TokenSaverToolWindow"/>. Edits two stores: the MCP server's own
/// settings file (<see cref="McpSettingsFile"/>, read by the server at startup, so a restart applies them) and the
/// extension's config section (<see cref="BehaviorConfigManager"/>, used by the extension straight away).
/// Numbers are bound as text and checked on Save; every field is checked and each bad one gets its own message.
/// </summary>
[DataContract]
internal sealed class SettingsViewModel : NotifyPropertyChangedObject
{
    private const string TestExplorer = "Test Explorer";
    private const string DotnetTest = "dotnet test";

    private bool _loading;
    private string _status = "";
    private string _restartNotice = "";

    // Connection
    private string _modelServerUrl = "";
    private string _helperModel = "";
    private string _mcpPort = "";

    // Features
    private bool _summarizeOutput;
    private bool _allowTerse;
    private bool _trackSavings;
    private bool _cleanPrompts;
    private bool _buildContextAtStartup;
    private bool _registerWithClients;
    private bool _autoWorkflow;
    private string _testRunner = TestExplorer;

    // Limits and endpoints
    private string _contextLimit = "";
    private string _endpointStrategy = "Failover";
    private string _endpointsError = "";

    // Visual Studio extension
    private bool _autostart;
    private bool _extensionDebugLogging;
    private string _mcpStartTimeout = "";
    private string _simpleModelServerStartTimeout = "";
    private string _simpleModelServerHealthUrl = "";
    private string _simpleModelServerExePath = "";

    // Advanced
    private string _healthCheckSeconds = "";
    private string _workspaceRoot = "";
    private string _behaviorConfigPath = "";
    private bool _mcpDebugLogging;

    // Per-field messages
    private string _modelServerUrlError = "";
    private string _mcpPortError = "";
    private string _contextLimitError = "";
    private string _mcpStartTimeoutError = "";
    private string _simpleModelServerStartTimeoutError = "";
    private string _simpleModelServerHealthUrlError = "";
    private string _healthCheckError = "";

    public SettingsViewModel()
    {
        SaveCommand = new AsyncCommand((_, _) =>
        {
            Save();
            return Task.CompletedTask;
        })
        { CanExecute = false };
        DiscardCommand = new AsyncCommand((_, _) =>
        {
            Load();
            return Task.CompletedTask;
        })
        { CanExecute = false };
        RestartCommand = new AsyncCommand(async (_, _) =>
        {
            Status = "Restarting TokenSaver MCP...";
            Status = await ServerControl.RestartAsync().ConfigureAwait(false);
            RestartNotice = "";
        });
        AddEndpointCommand = new AsyncCommand((_, _) =>
        {
            // The first endpoint added is the primary one.
            var row = new EndpointRow(OnRowChanged, RemoveRow) { IsPrimary = Endpoints.Count == 0 };
            Endpoints.Add(row);
            MarkDirty();
            return Task.CompletedTask;
        });
    }

    [DataMember] public AsyncCommand SaveCommand { get; }

    [DataMember] public AsyncCommand DiscardCommand { get; }

    [DataMember] public AsyncCommand RestartCommand { get; }

    [DataMember] public AsyncCommand AddEndpointCommand { get; }

    [DataMember] public IReadOnlyList<string> TestRunners { get; } = [TestExplorer, DotnetTest];

    // Names of TokenSaver.Common.Core EndpointSelectStrategy. The extension doesn't reference that assembly.
    [DataMember] public IReadOnlyList<string> EndpointStrategies { get; } = ["Failover", "RoundRobin", "Explicit"];

    [DataMember] public ObservableList<EndpointRow> Endpoints { get; } = [];

    [DataMember] public string Status { get => _status; set => SetProperty(ref _status, value); }

    /// <summary>Set after a save that changed MCP server settings; cleared by Restart.</summary>
    [DataMember] public string RestartNotice { get => _restartNotice; set => SetProperty(ref _restartNotice, value); }

    // ── Connection ──
    [DataMember] public string ModelServerUrl { get => _modelServerUrl; set => Set(ref _modelServerUrl, value); }

    [DataMember] public string HelperModel { get => _helperModel; set => Set(ref _helperModel, value); }

    [DataMember] public string McpPort { get => _mcpPort; set => Set(ref _mcpPort, value); }

    // ── Features ──
    [DataMember] public bool SummarizeOutput { get => _summarizeOutput; set => Set(ref _summarizeOutput, value); }

    [DataMember] public bool AllowTerse { get => _allowTerse; set => Set(ref _allowTerse, value); }

    [DataMember] public bool TrackSavings { get => _trackSavings; set => Set(ref _trackSavings, value); }

    [DataMember] public bool CleanPrompts { get => _cleanPrompts; set => Set(ref _cleanPrompts, value); }

    [DataMember] public bool BuildContextAtStartup { get => _buildContextAtStartup; set => Set(ref _buildContextAtStartup, value); }

    [DataMember] public bool RegisterWithClients { get => _registerWithClients; set => Set(ref _registerWithClients, value); }

    [DataMember] public bool AutoWorkflow { get => _autoWorkflow; set => Set(ref _autoWorkflow, value); }

    [DataMember] public string TestRunner { get => _testRunner; set => Set(ref _testRunner, value); }

    // ── Limits and endpoints ──
    [DataMember] public string ContextLimit { get => _contextLimit; set => Set(ref _contextLimit, value); }

    [DataMember] public string EndpointStrategy { get => _endpointStrategy; set => Set(ref _endpointStrategy, value); }

    [DataMember] public string EndpointsError { get => _endpointsError; set => SetProperty(ref _endpointsError, value); }

    // ── Visual Studio extension ──
    [DataMember] public bool Autostart { get => _autostart; set => Set(ref _autostart, value); }

    [DataMember] public bool ExtensionDebugLogging { get => _extensionDebugLogging; set => Set(ref _extensionDebugLogging, value); }

    [DataMember] public string McpStartTimeout { get => _mcpStartTimeout; set => Set(ref _mcpStartTimeout, value); }

    [DataMember] public string SimpleModelServerStartTimeout { get => _simpleModelServerStartTimeout; set => Set(ref _simpleModelServerStartTimeout, value); }

    [DataMember] public string SimpleModelServerHealthUrl { get => _simpleModelServerHealthUrl; set => Set(ref _simpleModelServerHealthUrl, value); }

    [DataMember] public string SimpleModelServerExePath { get => _simpleModelServerExePath; set => Set(ref _simpleModelServerExePath, value); }

    // ── Advanced ──
    [DataMember] public string HealthCheckSeconds { get => _healthCheckSeconds; set => Set(ref _healthCheckSeconds, value); }

    [DataMember] public string WorkspaceRoot { get => _workspaceRoot; set => Set(ref _workspaceRoot, value); }

    [DataMember] public string BehaviorConfigPath { get => _behaviorConfigPath; set => Set(ref _behaviorConfigPath, value); }

    [DataMember] public bool McpDebugLogging { get => _mcpDebugLogging; set => Set(ref _mcpDebugLogging, value); }

    // ── Messages under fields ──
    [DataMember] public string ModelServerUrlError { get => _modelServerUrlError; set => SetProperty(ref _modelServerUrlError, value); }

    [DataMember] public string McpPortError { get => _mcpPortError; set => SetProperty(ref _mcpPortError, value); }

    [DataMember] public string ContextLimitError { get => _contextLimitError; set => SetProperty(ref _contextLimitError, value); }

    [DataMember] public string McpStartTimeoutError { get => _mcpStartTimeoutError; set => SetProperty(ref _mcpStartTimeoutError, value); }

    [DataMember] public string SimpleModelServerStartTimeoutError { get => _simpleModelServerStartTimeoutError; set => SetProperty(ref _simpleModelServerStartTimeoutError, value); }

    [DataMember] public string SimpleModelServerHealthUrlError { get => _simpleModelServerHealthUrlError; set => SetProperty(ref _simpleModelServerHealthUrlError, value); }

    [DataMember] public string HealthCheckError { get => _healthCheckError; set => SetProperty(ref _healthCheckError, value); }

    /// <summary>
    /// Reads both stores into the form and discards edits. Not called from the constructor: this object is built while
    /// Visual Studio registers the extension's commands, where a file error would fail every command.
    /// </summary>
    public void Load()
    {
        _loading = true;
        try
        {
            ClearErrors();
            LoadExtension();
            LoadMcp();
        }
        finally
        {
            _loading = false;
        }

        SetDirty(false);
        Status = Status.StartsWith("Could not", StringComparison.Ordinal) ? Status : "";
    }

    private void LoadExtension()
    {
        ExtensionBehaviorConfig settings;
        try
        {
            settings = BehaviorConfigManager.GetExtensionSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Status = $"Could not read the extension settings: {ex.Message}";
            return;
        }

        Autostart = settings.Autostart ?? true;
        ExtensionDebugLogging = settings.VerboseLogging ?? false;
        McpStartTimeout = Text(settings.Server?.StartTimeout ?? ExtensionSettings.DefaultMcpStartTimeoutSeconds);
        SimpleModelServerStartTimeout = Text(settings.SimpleModelServer?.StartTimeout ?? ExtensionSettings.DefaultSimpleModelServerStartTimeoutSeconds);
        SimpleModelServerHealthUrl = settings.SimpleModelServer?.HealthUrl?.ToString() ?? "http://localhost:11435/health";
        SimpleModelServerExePath = settings.SimpleModelServer?.ExePath ?? "";
    }

    private void LoadMcp()
    {
        JsonObject root;
        try
        {
            root = McpSettingsFile.Load();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
        {
            Status = $"Could not read {McpSettingsFile.Path}: {ex.Message}";
            return;
        }

        ModelServerUrl = Str(root, "OllamaUrl", "http://localhost:11435");
        HelperModel = Str(root, "OllamaModel", "HelperModel");
        McpPort = Text(Num(McpSettingsFile.Get(root, "ServerPort"), ExtensionSettings.DefaultMcpPort));

        SummarizeOutput = Flag(McpSettingsFile.GetPath(root, "TokenEconomy:EnableDistill"), true);
        AllowTerse = Flag(McpSettingsFile.GetPath(root, "TokenEconomy:EnableTerse"), true);
        TrackSavings = Flag(McpSettingsFile.GetPath(root, "TokenEconomy:EnableSavingsTracking"), true);
        CleanPrompts = Flag(McpSettingsFile.GetPath(root, "ContextHelper:AutoIntercept"), true);
        BuildContextAtStartup = Flag(McpSettingsFile.GetPath(root, "ContextHelper:AutoWarmOnStartup"), true);
        RegisterWithClients = Flag(McpSettingsFile.Get(root, "AutoRegisterProviders"), true);
        AutoWorkflow = Flag(McpSettingsFile.GetPath(root, "CodeHelper:AutoEnabled"), false);
        TestRunner = Str(root, "IdeTestRunMode", "TestExplorer") == "DotnetTest" ? DotnetTest : TestExplorer;

        // The server has two context limits (the pipeline's and the cleaner's) and both default to 120,000. One
        // field sets both; show the pipeline's, which is the one that sizes built context.
        ContextLimit = Text(Num(McpSettingsFile.Get(root, "ContextSizeLimit"), Num(McpSettingsFile.GetPath(root, "ContextHelper:ContextSizeLimit"), 120_000)));
        EndpointStrategy = Str(root, "EndpointSelectStrategy", "Failover");
        LoadEndpoints(McpSettingsFile.Get(root, "Endpoints") as JsonArray);

        HealthCheckSeconds = Text(TimeSpan.TryParse(Str(root, "HealthCheckInterval", "00:00:30"), CultureInfo.InvariantCulture, out var interval) ? (int)interval.TotalSeconds : 30);
        WorkspaceRoot = Str(root, "WorkspaceRoot", "");
        BehaviorConfigPath = Str(root, "BehaviorConfigPath", "");
        McpDebugLogging = Flag(McpSettingsFile.Get(root, "VerboseLogging"), false);
    }

    private void LoadEndpoints(JsonArray? array)
    {
        Endpoints.Clear();
        if (array is null)
        {
            return;
        }

        foreach (var item in array.OfType<JsonObject>())
        {
            var row = NewRow(new EndpointRow(OnRowChanged, RemoveRow));
            row.Name = item["Name"]?.GetValue<string>() ?? "";
            row.Url = item["Url"]?.GetValue<string>() ?? "";
            row.IsPrimary = item["Primary"]?.GetValue<bool>() ?? false;
            row.Models = string.Join(", ", (item["Models"] as JsonArray)?.Select(m => m?.GetValue<string>() ?? "") ?? []);
        }
    }

    // A new row is wired to the list but not shown until it is added; loading adds it here.
    private EndpointRow NewRow(EndpointRow row)
    {
        if (_loading)
        {
            Endpoints.Add(row);
        }

        return row;
    }

    private void Save()
    {
        if (!Validate(out var port, out var contextLimit, out var healthSeconds, out var mcpStart, out var smsStart))
        {
            Status = "Not saved: fix the fields marked below.";
            return;
        }

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

        var before = root.ToJsonString();
        ApplyMcp(root, port, contextLimit, healthSeconds);
        var mcpChanged = root.ToJsonString() != before;

        try
        {
            var current = BehaviorConfigManager.GetExtensionSettings();
            BehaviorConfigManager.SaveExtensionSettings(current with
            {
                Autostart = Autostart,
                VerboseLogging = ExtensionDebugLogging,
                Server = current.Server! with { StartTimeout = mcpStart },
                SimpleModelServer = new ExtensionSimpleModelServerConfig
                {
                    ExePath = SimpleModelServerExePath.Trim(),
                    HealthUrl = new Uri(SimpleModelServerHealthUrl.Trim(), UriKind.Absolute),
                    StartTimeout = smsStart,
                },
            });

            if (mcpChanged)
            {
                McpSettingsFile.Save(root);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ExtensionLog.Logger.LogException(ex);
            Status = $"Not saved: {ex.Message}";
            return;
        }

        SetDirty(false);
        Status = "Saved.";
        if (mcpChanged)
        {
            RestartNotice = "TokenSaver MCP settings changed. Restart the server to apply them.";
        }
    }

    private void ApplyMcp(JsonObject root, int port, int contextLimit, int healthSeconds)
    {
        McpSettingsFile.Set(root, "OllamaUrl", JsonValue.Create(ModelServerUrl.Trim()));
        McpSettingsFile.Set(root, "OllamaModel", JsonValue.Create(HelperModel.Trim()));
        McpSettingsFile.Set(root, "ServerPort", JsonValue.Create(port));
        McpSettingsFile.SetPath(root, "TokenEconomy:EnableDistill", JsonValue.Create(SummarizeOutput));
        McpSettingsFile.SetPath(root, "TokenEconomy:EnableTerse", JsonValue.Create(AllowTerse));
        McpSettingsFile.SetPath(root, "TokenEconomy:EnableSavingsTracking", JsonValue.Create(TrackSavings));
        McpSettingsFile.SetPath(root, "ContextHelper:AutoIntercept", JsonValue.Create(CleanPrompts));
        McpSettingsFile.SetPath(root, "ContextHelper:AutoWarmOnStartup", JsonValue.Create(BuildContextAtStartup));
        McpSettingsFile.Set(root, "AutoRegisterProviders", JsonValue.Create(RegisterWithClients));
        McpSettingsFile.SetPath(root, "CodeHelper:AutoEnabled", JsonValue.Create(AutoWorkflow));
        McpSettingsFile.Set(root, "IdeTestRunMode", JsonValue.Create(TestRunner == DotnetTest ? "DotnetTest" : "TestExplorer"));

        McpSettingsFile.Set(root, "ContextSizeLimit", JsonValue.Create(contextLimit));
        McpSettingsFile.SetPath(root, "ContextHelper:ContextSizeLimit", JsonValue.Create(contextLimit));
        McpSettingsFile.Set(root, "EndpointSelectStrategy", JsonValue.Create(string.IsNullOrWhiteSpace(EndpointStrategy) ? "Failover" : EndpointStrategy));
        McpSettingsFile.Set(root, "Endpoints", EndpointsAsJson());

        McpSettingsFile.Set(root, "HealthCheckInterval", JsonValue.Create(TimeSpan.FromSeconds(healthSeconds).ToString("c", CultureInfo.InvariantCulture)));
        McpSettingsFile.Set(root, "WorkspaceRoot", JsonValue.Create(WorkspaceRoot.Trim()));
        McpSettingsFile.Set(root, "BehaviorConfigPath", JsonValue.Create(BehaviorConfigPath.Trim()));
        McpSettingsFile.Set(root, "VerboseLogging", JsonValue.Create(McpDebugLogging));
    }

    private JsonArray EndpointsAsJson()
    {
        var array = new JsonArray();
        foreach (var row in Endpoints)
        {
            var models = new JsonArray();
            foreach (var model in row.Models.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                models.Add(model);
            }

            array.Add(new JsonObject
            {
                ["Name"] = row.Name.Trim(),
                ["Url"] = row.Url.Trim(),
                ["Primary"] = row.IsPrimary,
                ["Models"] = models,
            });
        }

        return array;
    }

    /// <summary>Checks every field, putting a message under each bad one. True when all are fine.</summary>
    private bool Validate(out int port, out int contextLimit, out int healthSeconds, out int mcpStart, out int smsStart)
    {
        ClearErrors();

        ModelServerUrlError = UrlError(ModelServerUrl);
        SimpleModelServerHealthUrlError = UrlError(SimpleModelServerHealthUrl);
        McpPortError = IntError(McpPort, 1, 65535, out port);
        ContextLimitError = IntError(ContextLimit, 1, int.MaxValue, out contextLimit);
        HealthCheckError = IntError(HealthCheckSeconds, 1, int.MaxValue, out healthSeconds);
        McpStartTimeoutError = IntError(McpStartTimeout, 1, int.MaxValue, out mcpStart);
        SimpleModelServerStartTimeoutError = IntError(SimpleModelServerStartTimeout, 1, int.MaxValue, out smsStart);
        var endpointsOk = ValidateEndpoints();

        return endpointsOk
            && new[] { ModelServerUrlError, SimpleModelServerHealthUrlError, McpPortError, ContextLimitError, HealthCheckError, McpStartTimeoutError, SimpleModelServerStartTimeoutError }
                .All(string.IsNullOrEmpty);
    }

    private bool ValidateEndpoints()
    {
        var ok = true;
        foreach (var row in Endpoints)
        {
            row.Error = row.Name.Trim().Length == 0 ? "Give the endpoint a name."
                : UrlError(row.Url);
            ok &= row.Error.Length == 0;
        }

        if (ok && Endpoints.Count > 0 && Endpoints.Count(r => r.IsPrimary) != 1)
        {
            EndpointsError = "Mark exactly one endpoint as primary.";
            ok = false;
        }

        return ok;
    }

    private static string UrlError(string? text) =>
        Uri.TryCreate((text ?? "").Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? ""
            : "Enter a full address, e.g. http://localhost:11435";

    private static string IntError(string? text, int min, int max, out int value)
    {
        if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return "Enter a whole number.";
        }

        return value < min || value > max
            ? max == int.MaxValue ? $"Enter a number of {min} or more." : $"Enter a number from {min} to {max}."
            : "";
    }

    private void ClearErrors()
    {
        ModelServerUrlError = "";
        McpPortError = "";
        ContextLimitError = "";
        McpStartTimeoutError = "";
        SimpleModelServerStartTimeoutError = "";
        SimpleModelServerHealthUrlError = "";
        HealthCheckError = "";
        EndpointsError = "";
        foreach (var row in Endpoints)
        {
            row.Error = "";
        }
    }

    // Marking one endpoint primary clears the others, so the list never has two.
    private void OnRowChanged(EndpointRow changed)
    {
        if (_loading)
        {
            return;
        }

        if (changed.IsPrimary)
        {
            foreach (var other in Endpoints.Where(r => !ReferenceEquals(r, changed) && r.IsPrimary))
            {
                other.IsPrimary = false;
            }
        }

        MarkDirty();
    }

    private void RemoveRow(EndpointRow row)
    {
        Endpoints.Remove(row);
        MarkDirty();
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value, name) && !_loading)
        {
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        if (_loading)
        {
            return;
        }

        SetDirty(true);
        Status = "Unsaved changes.";
    }

    private void SetDirty(bool dirty)
    {
        SaveCommand.CanExecute = dirty;
        DiscardCommand.CanExecute = dirty;
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Str(JsonObject root, string key, string fallback) =>
        McpSettingsFile.Get(root, key) is JsonValue value && value.TryGetValue<string>(out var text) ? text : fallback;

    private static bool Flag(JsonNode? node, bool fallback) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;

    private static int Num(JsonNode? node, int fallback) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : fallback;
}
