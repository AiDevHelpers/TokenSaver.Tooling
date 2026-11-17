#pragma warning disable CEE0027 // Command and menu text is not localised in this release; tracked separately.
#pragma warning disable CA1812 // The commands are instantiated by the Visual Studio extension host via reflection.
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.Pages;

namespace TokenSaver.Vs.Extension.Commands;

// The three commands below open the TokenSaver tool window (see TokenSaverToolWindow) on one of its tabs.
// Constructors must take only out-of-process types: a classic VSSDK type here fails the whole command set.

/// <summary>Selects a tab of the TokenSaver tool window and shows the window.</summary>
internal static class OpenPage
{
    public static Task ShowAsync(VisualStudioExtensibility extensibility, TokenSaverWindowViewModel window, int page, CancellationToken ct)
    {
        ExtensionLog.Logger.LogInfo($"Opening TokenSaver window on page {page}");
        window.ShowPage(page);
        return extensibility.Shell().ShowToolWindowAsync<TokenSaverToolWindow>(activate: true, ct);
    }
}

/// <summary>Opens the Settings tab.</summary>
[VisualStudioContribution]
internal sealed class OpenSettingsCommand(VisualStudioExtensibility extensibility, TokenSaverWindowViewModel window) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Settings...");

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken) =>
        OpenPage.ShowAsync(this.Extensibility, window, TokenSaverWindowViewModel.SettingsPage, cancellationToken);
}

/// <summary>Opens the Services tab: start, stop, install and uninstall the two Windows services.</summary>
[VisualStudioContribution]
internal sealed class OpenServicesCommand(VisualStudioExtensibility extensibility, TokenSaverWindowViewModel window) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Services...");

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken) =>
        OpenPage.ShowAsync(this.Extensibility, window, TokenSaverWindowViewModel.ServicesPage, cancellationToken);
}

/// <summary>Opens the Logs tab: a live tail of the TokenSaver log files.</summary>
[VisualStudioContribution]
internal sealed class OpenLogsCommand(VisualStudioExtensibility extensibility, TokenSaverWindowViewModel window) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Logs...");

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken) =>
        OpenPage.ShowAsync(this.Extensibility, window, TokenSaverWindowViewModel.LogsPage, cancellationToken);
}
