#pragma warning disable CEE0027 // Command and menu text is not localised in this release; tracked separately.
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

using TokenSaver.Vs.Extension.Chat;
using TokenSaver.Vs.Extension.Settings;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>Opens the SimpleModelServer chat window (see <see cref="ChatWindow"/>) as a non-modal WPF
/// window, so it stays open alongside the editor for a running conversation.</summary>
[VisualStudioContribution]
public sealed class OpenChatCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Open Chat");

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        StaWindowLauncher.ShowNonModal(() => new ChatWindow());
        return Task.CompletedTask;
    }
}
