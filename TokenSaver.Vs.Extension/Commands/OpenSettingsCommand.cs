using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.VSSdkCompatibility;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

using TokenSaver.Vs.Extension.Options;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>Opens Tools &gt; Options &gt; TokenSaver &gt; General (see <see cref="TokenSaverOptionsPage"/>).</summary>
[VisualStudioContribution]
public sealed class OpenSettingsCommand(
    VisualStudioExtensibility extensibility,
    AsyncServiceProviderInjection<SVsShell, IVsShell> vsShell) : Command(extensibility)
{
    public override CommandConfiguration CommandConfiguration => new("Settings...");

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var shell = await vsShell.GetServiceAsync().ConfigureAwait(true);
        var packageGuid = new Guid(TokenSaverPackage.PackageGuidString);
        shell.LoadPackage(ref packageGuid, out var vsPackage);

        if (vsPackage is Package package)
        {
            package.ShowOptionPage(typeof(TokenSaverOptionsPage));
        }
    }
}
