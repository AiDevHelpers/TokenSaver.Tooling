using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TokenSaver.Vs.Extension.Configuration;

// Mirrors TokenSaver.Config's BehaviorConfigService/TokenSaverBehaviorConfig on the extension side
// (see the equivalent local mirror in TokenSaver.VsCodeExt/src/extension.ts), rather than taking a
// ProjectReference on TokenSaver.Config — this project intentionally stays a thin, self-contained
// client (see TokenSaver.Vs.Extension.csproj).
//
// Two files, local wins over global on any set property:
//   - {workspaceRoot}/.tokensaver/config.json  (local)
//   - %ProgramData%/TokenSaver/config.json     (global)

public enum ConfigScope
{
    Local,
    Global,
}

public sealed record RepoHelperBehaviorConfig
{
    [JsonPropertyName("addTokenSaverToGitignore")]
    public bool? AddTokenSaverToGitignore { get; init; }

    [JsonPropertyName("optimizeInstructions")]
    public bool? OptimizeInstructions { get; init; }
}

public sealed record TokenSaverBehaviorConfig
{
    [JsonPropertyName("repoHelper")]
    public RepoHelperBehaviorConfig? RepoHelper { get; init; }

    [JsonPropertyName("extension")]
    public ExtensionBehaviorConfig? Extension { get; init; }
}

// Mirrors TokenSaver.Config's ExtensionBehaviorConfig / IExtensionSettings (see
// src/Core/TokenSaver.Config/TokenSaverBehaviorConfig.cs and Contracts/IExtensionSettings.cs) —
// same "thin client, no ProjectReference" reasoning as RepoHelperBehaviorConfig above. Only the
// settings the VS extension's own Settings dialog edits are mirrored here (global scope only —
// this extension has no reliable API to resolve a workspace root outside a command's editor
// context, so unlike the gitignore prompt these settings are IDE-wide, not per-workspace).
public sealed record ExtensionBehaviorConfig
{
    [JsonPropertyName("autostart")]
    public bool? Autostart { get; init; }

    [JsonPropertyName("verboseLogging")]
    public bool? VerboseLogging { get; init; }

    [JsonPropertyName("server")]
    public ExtensionServerConfig? Server { get; init; }

    [JsonPropertyName("draftModel")]
    public ExtensionDraftModelConfig? DraftModel { get; init; }
}

public sealed record ExtensionServerConfig
{
    [JsonPropertyName("port")]
    public int? Port { get; init; }

    [JsonPropertyName("startTimeout")]
    public int? StartTimeout { get; init; }

    [JsonPropertyName("idleTimeout")]
    public int? IdleTimeout { get; init; }
}

public sealed record ExtensionDraftModelConfig
{
    [JsonPropertyName("exePath")]
    public string? ExePath { get; init; }

    [JsonPropertyName("healthUrl")]
    public string? HealthUrl { get; init; }

    [JsonPropertyName("startTimeout")]
    public int? StartTimeout { get; init; }
}

public static class BehaviorConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static RepoHelperBehaviorConfig GetMergedRepoHelperConfig(string workspaceRoot)
    {
        var global = ReadConfigFile(GlobalConfigPath())?.RepoHelper;
        var local = ReadConfigFile(LocalConfigPath(workspaceRoot))?.RepoHelper;

        return new RepoHelperBehaviorConfig
        {
            AddTokenSaverToGitignore = local?.AddTokenSaverToGitignore ?? global?.AddTokenSaverToGitignore,
            OptimizeInstructions = local?.OptimizeInstructions ?? global?.OptimizeInstructions,
        };
    }

    /// <summary>Reads the global (%ProgramData%) extension settings, falling back to documented defaults for any unset leaf.</summary>
    public static ExtensionBehaviorConfig GetExtensionSettings()
    {
        var global = ReadConfigFile(GlobalConfigPath())?.Extension;

        return new ExtensionBehaviorConfig
        {
            Autostart = global?.Autostart ?? true,
            VerboseLogging = global?.VerboseLogging ?? false,
            Server = new ExtensionServerConfig
            {
                Port = global?.Server?.Port ?? 11436,
                StartTimeout = global?.Server?.StartTimeout ?? 30,
                IdleTimeout = global?.Server?.IdleTimeout ?? 0,
            },
            DraftModel = new ExtensionDraftModelConfig
            {
                ExePath = global?.DraftModel?.ExePath ?? "",
                HealthUrl = global?.DraftModel?.HealthUrl ?? "http://localhost:11435/health",
                StartTimeout = global?.DraftModel?.StartTimeout ?? 600,
            },
        };
    }

    /// <summary>Persists <paramref name="settings" /> to the global (%ProgramData%) config file, leaving the rest of the file untouched.</summary>
    public static void SaveExtensionSettings(ExtensionBehaviorConfig settings)
    {
        var path = GlobalConfigPath();
        var current = ReadConfigFile(path) ?? new TokenSaverBehaviorConfig();
        var updated = current with { Extension = settings };

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(updated, JsonOptions));
    }

    public static void UpdateConfigValue(
        string workspaceRoot,
        ConfigScope scope,
        Func<TokenSaverBehaviorConfig, TokenSaverBehaviorConfig> update)
    {
        var path = scope == ConfigScope.Global ? GlobalConfigPath() : LocalConfigPath(workspaceRoot);
        var current = ReadConfigFile(path) ?? new TokenSaverBehaviorConfig();
        var updated = update(current);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(updated, JsonOptions));
    }

    private static TokenSaverBehaviorConfig? ReadConfigFile(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<TokenSaverBehaviorConfig>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GlobalConfigPath()
    {
        var programData = Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData";
        return Path.Combine(programData, "TokenSaver", "config.json");
    }

    private static string LocalConfigPath(string workspaceRoot) =>
        Path.Combine(workspaceRoot, ".tokensaver", "config.json");
}
