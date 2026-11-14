using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;

using TokenSaver.Vs.Extension.Logging;

namespace TokenSaver.Vs.Extension.Workspace;

/// <summary>Creates a <see cref="TokenSaverExtension"/> for the open solution, with its notices shown in VS.</summary>
internal static class TokenSaverWorkspace
{
    /// <summary>
    /// Returns a <see cref="TokenSaverExtension"/> for the open solution, or <c>null</c> when no solution is
    /// open. The caller owns the returned instance and must dispose it.
    /// </summary>
    public static async Task<TokenSaverExtension?> CreateForSolutionAsync(
        VisualStudioExtensibility extensibility,
        CancellationToken cancellationToken)
    {
        var solutionFolder = await SolutionFolder.GetAsync(extensibility, cancellationToken).ConfigureAwait(false);
        if (solutionFolder is null)
        {
            ExtensionLog.Logger.LogInfo("No solution open; skipping TokenSaver workspace setup");
            return null;
        }

        return new TokenSaverExtension(
            solutionFolder,
            ExtensionLog.Logger,
            message => extensibility.Shell().ShowPromptAsync(message, PromptOptions.OK, cancellationToken));
    }
}
