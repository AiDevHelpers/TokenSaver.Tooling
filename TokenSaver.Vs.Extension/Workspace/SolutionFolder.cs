using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.ProjectSystem.Query;

namespace TokenSaver.Vs.Extension.Workspace;

/// <summary>Resolves the folder of the currently open Visual Studio solution.</summary>
internal static class SolutionFolder
{
    /// <summary>
    /// Returns the directory containing the open solution file, or <c>null</c> when no solution is open.
    /// </summary>
    public static async Task<string?> GetAsync(VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
    {
        var results = await extensibility.Workspaces()
            .QuerySolutionAsync(solutions => solutions.With(s => s.Path), cancellationToken)
            .ConfigureAwait(false);

        var solution = results.FirstOrDefault();
        if (solution is null || string.IsNullOrEmpty(solution.Path))
        {
            return null;
        }

        return Path.GetDirectoryName(solution.Path);
    }
}
