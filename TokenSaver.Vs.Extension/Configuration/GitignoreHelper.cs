using System.IO;
using System.Linq;

namespace TokenSaver.Vs.Extension.Configuration;

public static class GitignoreHelper
{
    private const string GitignoreEntry = ".tokensaver/";

    public static bool IsTokenSaverInGitignore(string workspaceRoot)
    {
        var path = Path.Combine(workspaceRoot, ".gitignore");
        if (!File.Exists(path)) return false;

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Any(line => line is ".tokensaver" or ".tokensaver/" or "/.tokensaver" or "/.tokensaver/");
    }

    public static void AddTokenSaverToGitignore(string workspaceRoot)
    {
        var path = Path.Combine(workspaceRoot, ".gitignore");
        var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var needsNewline = existing.Length > 0 && !existing.EndsWith('\n');
        File.AppendAllText(path, (needsNewline ? "\n" : string.Empty) + GitignoreEntry + "\n");
    }
}
