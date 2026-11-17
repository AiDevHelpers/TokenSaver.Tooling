#pragma warning disable CEE0027 // Command and menu text is not localised in this release; tracked separately.
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

using TokenSaver.Vs.Extension.Settings;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>Opens the TokenSaver settings tool window (see <see cref="SettingsToolWindow"/>).</summary>
[VisualStudioContribution]
public sealed class OpenSettingsCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Settings...");

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var logger = TokenSaver.Vs.Extension.Logging.ExtensionLog.Logger;
        logger.LogInfo("Opening TokenSaver settings");

        using var tokenSaver = await TokenSaver.Vs.Extension.Workspace.TokenSaverWorkspace
            .CreateForSolutionAsync(this.Extensibility, cancellationToken).ConfigureAwait(false);
        if (tokenSaver is not null)
        {
            await tokenSaver.InitializeAsync().ConfigureAwait(false);
        }

        await this.Extensibility.Shell().ShowToolWindowAsync<SettingsToolWindow>(activate: true, cancellationToken)
            .ConfigureAwait(false);
    }
}
