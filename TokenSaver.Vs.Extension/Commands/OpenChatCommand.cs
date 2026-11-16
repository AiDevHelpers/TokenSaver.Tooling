#pragma warning disable CEE0027 // Command and menu text is not localised in this release; tracked separately.
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

using TokenSaver.Vs.Extension.Chat;
using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.Settings;
using TokenSaver.Vs.Extension.Workspace;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>Opens the SimpleModelServer chat window (see <see cref="ChatWindow"/>) as a non-modal WPF
/// window, so it stays open alongside the editor for a running conversation.</summary>
[VisualStudioContribution]
public sealed class OpenChatCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Open Chat") { Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu] };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        ExtensionLog.Logger.LogInfo("Opening SimpleModelServer chat window");

        using var tokenSaver = await TokenSaverWorkspace.CreateForSolutionAsync(this.Extensibility, cancellationToken)
            .ConfigureAwait(false);
        if (tokenSaver is not null)
        {
            await tokenSaver.InitializeAsync().ConfigureAwait(false);
        }

        StaWindowLauncher.ShowNonModal(() => new ChatWindow());
    }
}
