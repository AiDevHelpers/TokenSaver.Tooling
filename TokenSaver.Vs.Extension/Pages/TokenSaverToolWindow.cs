#pragma warning disable CEE0027 // Tool window text is not localised in this release; tracked separately.
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace TokenSaver.Vs.Extension.Pages;

/// <summary>
/// The TokenSaver tool window, with Settings, Services and Logs tabs. Opened by the Settings, Services and Logs
/// commands (see <see cref="Commands.OpenPageCommands"/>). The extension runs out of process, so the window is
/// Remote UI: the XAML in TokenSaverToolWindowContent.xaml renders inside Visual Studio and binds to
/// <see cref="TokenSaverWindowViewModel"/> in the extension process.
/// </summary>
[VisualStudioContribution]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by the Visual Studio extension host via reflection.")]
internal sealed class TokenSaverToolWindow : ToolWindow
{
    private readonly TokenSaverWindowViewModel _viewModel;
    private TokenSaverToolWindowContent? _content;

    public TokenSaverToolWindow(VisualStudioExtensibility extensibility, TokenSaverWindowViewModel viewModel)
        : base(extensibility)
    {
        _viewModel = viewModel;
        Title = "TokenSaver";
    }

    /// <inheritdoc/>
    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DocumentWell,
    };

    /// <inheritdoc/>
    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        // Visual Studio can also restore the window at startup, without a command having prepared the pages.
        _viewModel.Prepare();
        _content ??= new TokenSaverToolWindowContent(_viewModel);
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
