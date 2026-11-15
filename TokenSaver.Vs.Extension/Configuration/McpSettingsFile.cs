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

    /// <summary>Reads a nested key under the section, e.g. "ContextHelper:AutoIntercept". Null when any part is missing.</summary>
    public static JsonNode? GetPath(JsonObject root, string path)
    {
        JsonNode? node = root[SectionName];
        foreach (var part in path.Split(':'))
        {
            node = (node as JsonObject)?[part];
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    /// <summary>Writes a nested key under the section, creating intermediate objects as needed.</summary>
    public static void SetPath(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split(':');
        var current = EnsureObject(root, SectionName);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            current = EnsureObject(current, parts[i]);
        }

        current[parts[^1]] = value;
    }

    private static JsonObject EnsureObject(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[key] = created;
        return created;
    }

    public static void Save(JsonObject root)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, root.ToJsonString(JsonOptions));
    }
}
