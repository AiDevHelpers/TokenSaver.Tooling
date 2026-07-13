using System.Globalization;
using System.Windows;

using TokenSaver.Vs.Extension.Configuration;

namespace TokenSaver.Vs.Extension.Settings;

/// <summary>
/// WPF settings dialog for the TokenSaver Visual Studio extension. Edits
/// <see cref="ExtensionBehaviorConfig"/>, persisted to the global (%ProgramData%) config file via
/// <see cref="BehaviorConfigManager"/> — see that type for why this is global-only rather than
/// per-workspace.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Load();
    }

    private void Load()
    {
        var settings = BehaviorConfigManager.GetExtensionSettings();

        AutostartCheckBox.IsChecked = settings.Autostart ?? true;
        VerboseLoggingCheckBox.IsChecked = settings.VerboseLogging ?? false;
        PortTextBox.Text = (settings.Server?.Port ?? 11436).ToString(CultureInfo.InvariantCulture);
        StartTimeoutTextBox.Text = (settings.Server?.StartTimeout ?? 30).ToString(CultureInfo.InvariantCulture);
        IdleTimeoutTextBox.Text = (settings.Server?.IdleTimeout ?? 0).ToString(CultureInfo.InvariantCulture);
        DraftModelExePathTextBox.Text = settings.DraftModel?.ExePath ?? "";
        DraftModelHealthUrlTextBox.Text = settings.DraftModel?.HealthUrl ?? "http://localhost:11435/health";
        DraftModelStartTimeoutTextBox.Text = (settings.DraftModel?.StartTimeout ?? 600).ToString(CultureInfo.InvariantCulture);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseInt(PortTextBox.Text, out var port) ||
            !TryParseInt(StartTimeoutTextBox.Text, out var startTimeout) ||
            !TryParseInt(IdleTimeoutTextBox.Text, out var idleTimeout) ||
            !TryParseInt(DraftModelStartTimeoutTextBox.Text, out var draftModelStartTimeout))
        {
            StatusTextBlock.Text = "Timeouts and the port must be whole numbers.";
            return;
        }

        var settings = new ExtensionBehaviorConfig
        {
            Autostart = AutostartCheckBox.IsChecked ?? true,
            VerboseLogging = VerboseLoggingCheckBox.IsChecked ?? false,
            Server = new ExtensionServerConfig
            {
                Port = port,
                StartTimeout = startTimeout,
                IdleTimeout = idleTimeout,
            },
            DraftModel = new ExtensionDraftModelConfig
            {
                ExePath = DraftModelExePathTextBox.Text.Trim(),
                HealthUrl = DraftModelHealthUrlTextBox.Text.Trim(),
                StartTimeout = draftModelStartTimeout,
            },
        };

        BehaviorConfigManager.SaveExtensionSettings(settings);
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
