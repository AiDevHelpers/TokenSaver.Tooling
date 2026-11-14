using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

using Microsoft.VisualStudio.Shell;

using TokenSaver.Vs.Extension.Configuration;

namespace TokenSaver.Vs.Extension.Options;

/// <summary>
/// Tools &gt; Options &gt; TokenSaver &gt; General page. Edits two stores: the extension's own global behavior
/// config (<see cref="BehaviorConfigManager"/>) and the MCP server's settings file in %ProgramData%
/// (<see cref="McpSettingsFile"/>), which the MCP server loads as its configuration.
/// </summary>
public sealed class TokenSaverOptionsPage : UIElementDialogPage
{
    private TokenSaverOptionsControl? _control;

    protected override UIElement Child => _control ??= new TokenSaverOptionsControl();

    public override void LoadSettingsFromStorage()
    {
        _control ??= new TokenSaverOptionsControl();
        _control.Load(BehaviorConfigManager.GetExtensionSettings());

        try
        {
            _control.LoadMcp(McpSettingsFile.Load());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            _control.SetStatus($"Could not read {McpSettingsFile.Path}: {ex.Message}");
        }
    }

    public override void SaveSettingsToStorage()
    {
        if (_control is null)
        {
            return;
        }

        if (!_control.TryBuildSettings(out var settings, out _))
        {
            return;
        }

        BehaviorConfigManager.SaveExtensionSettings(settings);

        JsonObject root;
        try
        {
            root = McpSettingsFile.Load();
        }
        catch (JsonException ex)
        {
            // Never overwrite a file we could not read.
            _control.SetStatus($"Not saved: {McpSettingsFile.Path} is not valid JSON ({ex.Message}).");
            return;
        }

        if (!_control.TryApplyMcp(root, out var error))
        {
            _control.SetStatus(error ?? "Not saved: invalid MCP settings.");
            return;
        }

        McpSettingsFile.Save(root);
    }
}
