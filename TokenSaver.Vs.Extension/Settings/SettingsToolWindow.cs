#pragma warning disable CEE0027 // Tool window text is not localised in this release; tracked separately.
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace TokenSaver.Vs.Extension.Settings;

/// <summary>
/// TokenSaver settings tool window, opened by <see cref="Commands.OpenSettingsCommand"/>. The extension runs out of
/// process, so the form is Remote UI: the XAML in SettingsToolWindowContent.xaml renders inside Visual Studio and
/// binds to <see cref="SettingsViewModel"/> in the extension process.
/// </summary>
[VisualStudioContribution]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by the Visual Studio extension host via reflection.")]
internal sealed class SettingsToolWindow : ToolWindow
{
    private SettingsToolWindowContent? _content;

    public SettingsToolWindow(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
        Title = "TokenSaver Settings";
    }

    /// <inheritdoc/>
    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DocumentWell,
    };

    /// <inheritdoc/>
    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        // Built here, not in the constructor, so the settings files are read when the window opens.
        _content ??= new SettingsToolWindowContent(new SettingsViewModel());
        return Task.FromResult<IRemoteUserControl>(_content);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _content?.Dispose();
        }

        base.Dispose(disposing);
    }
}
