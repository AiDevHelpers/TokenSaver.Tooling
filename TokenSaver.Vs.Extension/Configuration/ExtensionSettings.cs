using System;
using System.IO;
using System.Text.Json;

namespace TokenSaver.Vs.Extension.Configuration;

/// <summary>
/// The settings the extension acts on, with their defaults, read fresh on every access so a change on the Settings
/// tab takes effect without a restart. The MCP port comes from the MCP server's own settings file, because that is
/// the port the server really listens on; everything else comes from the extension's config section.
/// A missing or unreadable file gives the defaults.
/// </summary>
internal static class ExtensionSettings
{
    public const int DefaultMcpPort = 11436;
    public const int DefaultMcpStartTimeoutSeconds = 30;
    public const int DefaultSimpleModelServerStartTimeoutSeconds = 600;

    /// <summary>Start the two services when the extension first loads.</summary>
    public static bool Autostart => Read(c => c.Autostart) ?? true;

    /// <summary>Log at Debug level. Read once, when the extension's logger is created.</summary>
    public static bool VerboseLogging => Read(c => c.VerboseLogging) ?? false;

    public static TimeSpan McpStartTimeout =>
        TimeSpan.FromSeconds(Positive(Read(c => c.Server?.StartTimeout), DefaultMcpStartTimeoutSeconds));

    public static TimeSpan SimpleModelServerStartTimeout =>
        TimeSpan.FromSeconds(Positive(Read(c => c.SimpleModelServer?.StartTimeout), DefaultSimpleModelServerStartTimeoutSeconds));

    /// <summary>SimpleModelServer.Server.exe: the configured path, or the MSI's install location when blank.</summary>
    public static string SimpleModelServerExePath
    {
        get
        {
            var configured = Read(c => c.SimpleModelServer?.ExePath);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            var programFiles = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
            return Path.Combine(programFiles, "SimpleModelServer", "SimpleModelServer.Server.exe");
        }
    }

    /// <summary>The port TokenSaver MCP listens on, from the MCP server's settings file.</summary>
    public static int McpPort
    {
        get
        {
            try
            {
                var port = McpSettingsFile.Get(McpSettingsFile.Load(), "ServerPort")?.GetValue<int>();
                return port is > 0 and <= 65535 ? port.Value : DefaultMcpPort;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
            {
                return DefaultMcpPort;
            }
        }
    }

    private static T? Read<T>(Func<ExtensionBehaviorConfig, T?> select)
    {
        try
        {
            return select(BehaviorConfigManager.GetExtensionSettings());
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    private static int Positive(int? value, int fallback) => value is > 0 ? value.Value : fallback;
}
