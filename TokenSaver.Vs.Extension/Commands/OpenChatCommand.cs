using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>
/// Writes an instructional message to the "DraftModel" Output Window channel and surfaces it via
/// a prompt dialog — the closest thing to a persistent chat window this extension has (Visual
/// Studio has no public SDK hook to register a custom chat/Copilot-model UI; see
/// <see cref="DraftModelCommandBase"/>'s remarks). The prompt is necessary because
/// <see cref="Microsoft.VisualStudio.Extensibility.Documents.OutputChannel"/> has no API to bring
/// the Output window into view — writing to a freshly created channel is otherwise invisible
/// until the user manually opens View &gt; Output and picks "DraftModel" from the source dropdown.
/// The per-selection commands under the "DraftModel" editor context-menu (Ask about Selection,
/// Explain Code, etc.) write their responses to this same channel.
/// </summary>
[VisualStudioContribution]
public sealed class OpenChatCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    private const string OutputChannelName = "DraftModel";

    public override CommandConfiguration CommandConfiguration => new("Open Chat");

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var outputChannel = await this.Extensibility.Views().Output
            .CreateOutputChannelAsync(OutputChannelName, cancellationToken)
            .ConfigureAwait(false);

        await outputChannel.WriteLineAsync(
            "--- DraftModel chat ---\n" +
            "Select code in the editor and use one of the DraftModel commands (right-click > DraftModel) " +
            "to ask about it, explain it, fix it, refactor it, generate docs/tests, or request a code review.")
            .ConfigureAwait(false);

        await this.Extensibility.Shell()
            .ShowPromptAsync(
                "DraftModel has no dedicated chat window yet. Select code in the editor and use " +
                "one of the DraftModel commands (right-click > DraftModel) to ask about it. " +
                "Responses stream to View > Output > \"DraftModel\".",
                PromptOptions.OK,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
