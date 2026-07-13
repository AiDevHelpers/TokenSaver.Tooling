using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.Shell;
using TokenSaver.Vs.Extension.Services;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>
/// Shared behavior for every DraftModel command: grab the active editor's selection (or whole
/// document), build the action-specific prompt, stream DraftModel's response into an Output
/// Window channel. Visual Studio has no public SDK hook to register a custom Copilot model
/// provider (unlike VS Code's languageModelChatProviders), so each Copilot-equivalent action gets
/// its own command against DraftModel directly instead.
/// </summary>
public abstract class DraftModelCommandBase : Command
{
    private const string OutputChannelName = "DraftModel";
    private static readonly Uri DefaultBaseUrl = new("http://localhost:11435");

    protected DraftModelCommandBase(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
    }

    protected abstract DraftModelAction Action { get; }

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var outputChannel = await this.Extensibility.Views().Output
            .CreateOutputChannelAsync(OutputChannelName, cancellationToken)
            .ConfigureAwait(false);

        using var textView = await this.Extensibility.Editor()
            .GetActiveTextViewAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (textView is null)
        {
            await this.Extensibility.Shell()
                .ShowPromptAsync("DraftModel: no active editor.", PromptOptions.OK, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var selection = textView.Selection;
        var range = selection.IsEmpty ? textView.Document.Text : selection.Extent;
        var code = range.CopyToString();

        if (string.IsNullOrWhiteSpace(code))
        {
            await this.Extensibility.Shell()
                .ShowPromptAsync("DraftModel: nothing to send (empty document).", PromptOptions.OK, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var languageId = Path.GetExtension(textView.FilePath)?.TrimStart('.');
        var messages = DraftModelPromptBuilder.BuildMessages(this.Action, code, languageId);

        await outputChannel.WriteLineAsync($"--- DraftModel: {this.Action} ---").ConfigureAwait(false);

        using var client = new HttpDraftModelClient(DefaultBaseUrl);
        var health = await client.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
        if (health.Status == DraftModelHealthStatus.Unreachable)
        {
            await outputChannel.WriteLineAsync(
                $"DraftModel is not reachable at {DefaultBaseUrl}. Start it via 'TokenSaver: Start TokenSaver MCP Server'.")
                .ConfigureAwait(false);
            return;
        }

        try
        {
            await foreach (var chunk in client.StreamChatAsync(messages, cancellationToken).ConfigureAwait(false))
            {
                if (chunk.ContentDelta.Length > 0)
                {
                    await outputChannel.WriteAsync(chunk.ContentDelta).ConfigureAwait(false);
                }
            }

            await outputChannel.WriteLineAsync(string.Empty).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException)
        {
            await outputChannel.WriteLineAsync($"DraftModel request failed: {ex.Message}").ConfigureAwait(false);
        }
    }
}
