using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace TokenSaver.Vs.Extension.WindowsService;

/// <summary>
/// Runs a WiX MSI bundled beside the extension assembly through msiexec, elevated with a UAC prompt. The MSI owns
/// how the Windows service is registered, so this does not duplicate sc.exe logic.
/// </summary>
public static class MsiInstaller
{
    /// <summary>Full path to a bundled installer, next to this extension's assembly.</summary>
    public static string BundledMsiPath(string fileName) =>
        Path.Combine(Path.GetDirectoryName(typeof(MsiInstaller).Assembly.Location) ?? AppContext.BaseDirectory, fileName);

    /// <summary>
    /// Runs <c>msiexec /i</c> (install) or <c>/x</c> (uninstall) quietly. Returns the msiexec exit code, or -1 if the
    /// UAC prompt was declined or msiexec could not start.
    /// </summary>
    public static Task<int> RunAsync(string operation, string msiPath) =>
        Task.Run(() =>
        {
            var startInfo = new ProcessStartInfo("msiexec.exe", $"{operation} \"{msiPath}\" /qn /norestart")
            {
                UseShellExecute = true,
                Verb = "runas",
            };

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return -1;
                }

                process.WaitForExit();
                return process.ExitCode;
            }
            catch (Win32Exception)
            {
                // UAC declined or elevation failed.
                return -1;
            }
        });
}
