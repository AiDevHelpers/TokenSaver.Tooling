using System.Globalization;
using System.Windows.Controls;

using TokenSaver.Vs.Extension.Configuration;

namespace TokenSaver.Vs.Extension.Options;

/// <summary>WPF content of <see cref="TokenSaverOptionsPage"/> — see that type's remarks.</summary>
public partial class TokenSaverOptionsControl : UserControl
{
    public TokenSaverOptionsControl()
    {
        InitializeComponent();
    }

    internal void Load(ExtensionBehaviorConfig settings)
    {
        AutostartCheckBox.IsChecked = settings.Autostart ?? true;
        VerboseLoggingCheckBox.IsChecked = settings.VerboseLogging ?? false;
        PortTextBox.Text = (settings.Server?.Port ?? 11436).ToString(CultureInfo.InvariantCulture);
        StartTimeoutTextBox.Text = (settings.Server?.StartTimeout ?? 30).ToString(CultureInfo.InvariantCulture);
        IdleTimeoutTextBox.Text = (settings.Server?.IdleTimeout ?? 0).ToString(CultureInfo.InvariantCulture);
        DraftModelExePathTextBox.Text = settings.DraftModel?.ExePath ?? "";
        DraftModelHealthUrlTextBox.Text = settings.DraftModel?.HealthUrl ?? "http://localhost:11435/health";
        DraftModelStartTimeoutTextBox.Text = (settings.DraftModel?.StartTimeout ?? 600).ToString(CultureInfo.InvariantCulture);
        StatusTextBlock.Text = string.Empty;
    }

    internal bool TryBuildSettings(out ExtensionBehaviorConfig settings, out string? error)
    {
        if (!TryParseInt(PortTextBox.Text, out var port) ||
            !TryParseInt(StartTimeoutTextBox.Text, out var startTimeout) ||
            !TryParseInt(IdleTimeoutTextBox.Text, out var idleTimeout) ||
            !TryParseInt(DraftModelStartTimeoutTextBox.Text, out var draftModelStartTimeout))
        {
            error = "Timeouts and the port must be whole numbers.";
            StatusTextBlock.Text = error;
            settings = new ExtensionBehaviorConfig();
            return false;
        }

        error = null;
        settings = new ExtensionBehaviorConfig
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
        return true;
    }

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
