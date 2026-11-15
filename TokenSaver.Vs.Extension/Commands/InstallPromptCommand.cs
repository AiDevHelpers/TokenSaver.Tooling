using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.WindowsService;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>Opens INSTALL_PROMPT.md (bundled beside the extension) so the user can paste it into their AI client.</summary>
[VisualStudioContribution]
public sealed class InstallPromptCommand(VisualStudioExtensibility extensibility) : Command(extensibility)
{
    public const string PromptFileName = "INSTALL_PROMPT.md";

    public override CommandConfiguration CommandConfiguration => new("Open TokenSaver Install Prompt");

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        ExtensionLog.Logger.LogInfo("Open install prompt requested");
        var path = MsiInstaller.BundledMsiPath(PromptFileName);
        if (!File.Exists(path))
        {
            await ServerControl.ShowAsync(this.Extensibility, $"TokenSaver install prompt not found at: {path}", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            ExtensionLog.Logger.LogException(ex);
            await ServerControl.ShowAsync(this.Extensibility, $"Could not open the install prompt: {ex.Message}. File: {path}", cancellationToken).ConfigureAwait(false);
        }
    }
}
