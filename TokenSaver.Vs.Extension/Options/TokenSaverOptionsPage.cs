using System.Windows;

using Microsoft.VisualStudio.Shell;

using TokenSaver.Vs.Extension.Configuration;

namespace TokenSaver.Vs.Extension.Options;

/// <summary>
/// Tools &gt; Options &gt; TokenSaver &gt; General page. Backed by the same
/// <see cref="BehaviorConfigManager"/> global config file as the older <c>SettingsWindow</c> WPF
/// dialog — this page supersedes that dialog as the primary settings surface (see
/// <see cref="Commands.OpenSettingsCommand"/>), but the manager itself is unchanged.
/// </summary>
public sealed class TokenSaverOptionsPage : UIElementDialogPage
{
    private TokenSaverOptionsControl? _control;

    protected override UIElement Child => _control ??= new TokenSaverOptionsControl();

    public override void LoadSettingsFromStorage()
    {
        _control ??= new TokenSaverOptionsControl();
        _control.Load(BehaviorConfigManager.GetExtensionSettings());
    }

    public override void SaveSettingsToStorage()
    {
        if (_control is null)
        {
            return;
        }

        if (_control.TryBuildSettings(out var settings, out _))
        {
            BehaviorConfigManager.SaveExtensionSettings(settings);
        }
    }
}
