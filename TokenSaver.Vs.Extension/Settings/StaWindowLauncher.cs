using System;
using System.Threading;
using System.Windows;

namespace TokenSaver.Vs.Extension.Settings;

/// <summary>
/// Shows a WPF <see cref="Window"/> from an out-of-process VisualStudio.Extensibility command.
/// Command execution runs on a thread-pool thread with no <see cref="System.Windows.Threading.Dispatcher"/>
/// and no ambient <see cref="Application"/>, so both entry points run on a dedicated STA thread
/// with its own dispatcher loop instead of relying on either.
/// </summary>
internal static class StaWindowLauncher
{
    public static void ShowDialog(Func<Window> createWindow)
    {
        var thread = new Thread(() =>
        {
            var window = createWindow();
            window.ShowDialog();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
    }

    /// <summary>
    /// Shows a non-modal window and returns immediately — used for windows meant to stay open
    /// alongside the editor (e.g. the DraftModel chat window) rather than block the caller.
    /// The STA thread pumps its own <see cref="System.Windows.Threading.Dispatcher"/> until the
    /// window closes, then exits.
    /// </summary>
    public static void ShowNonModal(Func<Window> createWindow)
    {
        var thread = new Thread(() =>
        {
            var window = createWindow();
            window.Closed += (_, _) => window.Dispatcher.InvokeShutdown();
            window.Show();
            System.Windows.Threading.Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }
}
