using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

using TokenSaver.Vs.Extension.Configuration;
using TokenSaver.Vs.Extension.Logging;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>
/// Flips the Visual Studio-only auto-workflow setting. Stored in the extension's own config section, which the
/// server does not read, so it changes nothing on the server side.
/// </summary>
[VisualStudioContribution]
public sealed class ToggleAutoWorkflowCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("%ToggleAutoWorkflow.DisplayName%") { Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu] };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var current = BehaviorConfigManager.GetExtensionSettings();
        var enabled = !(current.AutoWorkflow ?? false);
        BehaviorConfigManager.SaveExtensionSettings(current with { AutoWorkflow = enabled });
        ExtensionLog.Logger.LogInfo($"Auto-workflow (Visual Studio) set to {enabled}");

        await ServerControl.ShowAsync(
            this.Extensibility,
            $"TokenSaver auto-workflow (Visual Studio) is now {(enabled ? "enabled" : "disabled")}.",
            cancellationToken).ConfigureAwait(false);
    }
}
