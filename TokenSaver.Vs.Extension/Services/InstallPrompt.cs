using System.ComponentModel;
using System.Diagnostics;
using System.IO;

using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.WindowsService;

namespace TokenSaver.Vs.Extension.Services;

/// <summary>Opens INSTALL_PROMPT.md (bundled beside the extension) so the user can paste it into their AI client.</summary>
internal static class InstallPrompt
{
    public const string PromptFileName = "INSTALL_PROMPT.md";

    /// <summary>Opens the bundled prompt with the default app. Returns an error message, or null on success.</summary>
    public static string? Open()
    {
        ExtensionLog.Logger.LogInfo("Open install prompt requested");
        var path = MsiInstaller.BundledMsiPath(PromptFileName);
        if (!File.Exists(path))
        {
            return $"TokenSaver install prompt not found at: {path}";
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return null;
        }
        catch (Win32Exception ex)
        {
            ExtensionLog.Logger.LogException(ex);
            return $"Could not open the install prompt: {ex.Message}. File: {path}";
        }
    }
}
