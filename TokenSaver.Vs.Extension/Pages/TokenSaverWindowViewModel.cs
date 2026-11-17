using System;
using System.Runtime.Serialization;

using Microsoft.VisualStudio.Extensibility.UI;

namespace TokenSaver.Vs.Extension.Pages;

/// <summary>
/// Data context of <see cref="TokenSaverToolWindow"/>: the selected tab and one view model per tab. Registered as a
/// singleton so the menu commands and the window share it; a command picks the tab with <see cref="ShowPage"/>
/// before it shows the window.
/// </summary>
[DataContract]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Created by the extension's service provider.")]
internal sealed class TokenSaverWindowViewModel : NotifyPropertyChangedObject, IDisposable
{
    public const int SettingsPage = 0;
    public const int ServicesPage = 1;
    public const int LogsPage = 2;

    private int _selectedTab;
    private bool _settingsLoaded;

    [DataMember] public int SelectedTab { get => _selectedTab; set => SetProperty(ref _selectedTab, value); }

    [DataMember] public SettingsViewModel Settings { get; } = new();

    [DataMember] public ServicesViewModel Services { get; } = new();

    [DataMember] public LogsViewModel Logs { get; } = new();

    /// <summary>Selects a tab and brings every tab's content up to date.</summary>
    public void ShowPage(int page)
    {
        SelectedTab = page;
        Prepare();
    }

    /// <summary>Loads the settings once, reads the service states, and starts the log tail.</summary>
    public void Prepare()
    {
        if (!_settingsLoaded)
        {
            _settingsLoaded = true;
            Settings.Load();
        }

        _ = Services.RefreshAsync();
        Logs.Start();
    }

    public void Dispose() => Logs.Dispose();
}
