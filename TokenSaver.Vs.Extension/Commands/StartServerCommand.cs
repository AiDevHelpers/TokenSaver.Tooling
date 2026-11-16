using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.Workspace;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>Starts the TokenSaver.MCP server (the TokenSaverMCP Windows service) and reports whether it is reachable.</summary>
[VisualStudioContribution]
public sealed class StartServerCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("%StartServer.DisplayName%") { Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu] };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        ExtensionLog.Logger.LogInfo("Start TokenSaver MCP Server requested");

        using var tokenSaver = await TokenSaverWorkspace.CreateForSolutionAsync(this.Extensibility, cancellationToken)
            .ConfigureAwait(false);
        if (tokenSaver is null)
        {
            await this.Extensibility.Shell()
                .ShowPromptAsync("TokenSaver: open a solution first.", PromptOptions.OK, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await tokenSaver.StartServerAsync().ConfigureAwait(false);
        var reachable = await tokenSaver.CheckServerHealthAsync().ConfigureAwait(false);

        await this.Extensibility.Shell()
            .ShowPromptAsync(
                reachable
                    ? "TokenSaver MCP server is running and reachable."
                    : "TokenSaver MCP server did not respond. See the TokenSaver log for details.",
                PromptOptions.OK,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
