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
