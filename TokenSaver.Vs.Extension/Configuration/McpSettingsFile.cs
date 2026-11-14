using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenSaver.Vs.Extension.Configuration;

/// <summary>
/// Reads and writes the MCP server's settings file, %ProgramData%\TokenSaver\appsettings.json, which the MCP
/// server loads as its only configuration. Only the "TokenSaver" section's keys are changed; every other key
/// is preserved as-is.
/// </summary>
internal static class McpSettingsFile
{
    private const string SectionName = "TokenSaver";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Path =>
        System.IO.Path.Combine(Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData", "TokenSaver", "appsettings.json");

    /// <summary>
    /// Loads the file as a JSON object. A missing file gives an empty object. A malformed file throws
    /// <see cref="JsonException"/> so the caller can refuse to overwrite it.
    /// </summary>
    public static JsonObject Load()
    {
        if (!File.Exists(Path))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(File.ReadAllText(Path)) as JsonObject
            ?? throw new JsonException("The settings file is not a JSON object.");
    }

    public static JsonNode? Get(JsonObject root, string key) =>
        (root[SectionName] as JsonObject)?[key];

    public static void Set(JsonObject root, string key, JsonNode? value)
    {
        var section = root[SectionName] as JsonObject;
        if (section is null)
        {
            section = new JsonObject();
            root[SectionName] = section;
        }

        section[key] = value;
    }

    public static void Save(JsonObject root)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, root.ToJsonString(JsonOptions));
    }
}
