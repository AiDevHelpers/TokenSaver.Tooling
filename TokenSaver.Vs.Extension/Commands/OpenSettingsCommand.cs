using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

using TokenSaver.Vs.Extension.Settings;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>Opens the TokenSaver settings dialog — see <see cref="TokenSaverMenu"/>.</summary>
[VisualStudioContribution]
public sealed class OpenSettingsCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Settings...");

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        StaWindowLauncher.ShowDialog(() => new SettingsWindow());
        return Task.CompletedTask;
    }
}
