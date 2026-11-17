using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility.UI;

using TokenSaver.Vs.Extension.Logging;
using TokenSaver.Vs.Extension.Services;

namespace TokenSaver.Vs.Extension.Pages;

/// <summary>
/// Data context of the Logs tab of <see cref="TokenSaverToolWindow"/>. Lists the files in the TokenSaver logs folder
/// and shows the tail of one of them, re-reading it on a timer when the file changes. While
/// <see cref="FollowNewest"/> is on, the view moves to whichever file was written last.
/// </summary>
[DataContract]
internal sealed class LogsViewModel : NotifyPropertyChangedObject, IDisposable
{
    private const int TailBytes = 128 * 1024;
    private const int TailLines = 500;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private string _selectedFile = "";
    private bool _followNewest = true;
    private bool _newestFirst = true;
    private string _text = "";
    private string _status = "";

    private Timer? _timer;
    private int _polling;
    private bool _selectingInternally;
    private string _shownKey = "";

    public LogsViewModel()
    {
        RefreshCommand = new AsyncCommand((_, _) =>
        {
            _shownKey = "";
            Poll();
            return Task.CompletedTask;
        });
        OpenFolderCommand = new AsyncCommand((_, _) =>
        {
            Directory.CreateDirectory(ServerControl.LogsDirectory);
            using var explorer = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ServerControl.LogsDirectory}\"") { UseShellExecute = false });
            return Task.CompletedTask;
        });
    }

    [DataMember] public AsyncCommand RefreshCommand { get; }

    [DataMember] public AsyncCommand OpenFolderCommand { get; }

    /// <summary>File names in the logs folder, last written first.</summary>
    [DataMember] public ObservableList<string> Files { get; } = [];

    [DataMember]
    public string SelectedFile
    {
        get => _selectedFile;
        set
        {
            // The combo box reports null while its list is being replaced; that is not a choice.
            if (string.IsNullOrEmpty(value) || !SetProperty(ref _selectedFile, value) || _selectingInternally)
            {
                return;
            }

            // The user picked a file: stay on it instead of jumping back to the newest one.
            FollowNewest = false;
        }
    }

    [DataMember] public bool FollowNewest { get => _followNewest; set => SetProperty(ref _followNewest, value); }

    /// <summary>Newest line at the top. Remote UI cannot scroll a text box to its end, so this keeps new lines in view.</summary>
    [DataMember] public bool NewestFirst { get => _newestFirst; set => SetProperty(ref _newestFirst, value); }

    [DataMember] public string Text { get => _text; set => SetProperty(ref _text, value); }

    [DataMember] public string Status { get => _status; set => SetProperty(ref _status, value); }

    /// <summary>Starts the live tail. Safe to call again.</summary>
    public void Start()
    {
        _timer ??= new Timer(_ => Poll(), null, TimeSpan.Zero, PollInterval);
    }

    public void Dispose() => _timer?.Dispose();

    private void Poll()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            var folder = new DirectoryInfo(ServerControl.LogsDirectory);
            if (!folder.Exists)
            {
                Status = $"No logs yet: {folder.FullName} does not exist.";
                Text = "";
                return;
            }

            var files = folder.GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            var names = files.Select(f => f.Name).ToArray();
            if (!names.SequenceEqual(Files))
            {
                var keep = _selectedFile;
                Files.Clear();
                foreach (var name in names)
                {
                    Files.Add(name);
                }

                Select(keep);
            }

            if (files.Length == 0)
            {
                Status = $"No log files in {folder.FullName}.";
                Text = "";
                return;
            }

            if (FollowNewest || !names.Contains(_selectedFile))
            {
                Select(names[0]);
            }

            var file = files.First(f => f.Name == _selectedFile);
            var key = $"{file.Name}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{NewestFirst}";
            if (key == _shownKey)
            {
                return;
            }

            Text = ReadTail(file.FullName, NewestFirst);
            Status = $"{file.FullName} - last written {file.LastWriteTime:HH:mm:ss}, showing the last {TailLines} lines at most.";
            _shownKey = key;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not read the log: {ex.Message}";
        }
#pragma warning disable CA1031 // The timer must survive any failure: the next tick retries.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ExtensionLog.Logger.LogException(ex);
            Status = $"Could not read the log: {ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private void Select(string name)
    {
        _selectingInternally = true;
        try
        {
            SelectedFile = name;
        }
        finally
        {
            _selectingInternally = false;
        }
    }

    private static string ReadTail(string path, bool newestFirst)
    {
        // The server holds the file open for writing, so allow that while reading.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var cut = stream.Length > TailBytes;
        if (cut)
        {
            stream.Seek(-TailBytes, SeekOrigin.End);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (cut && lines.Count > 0)
        {
            // The first line starts mid-way through, where the tail was cut.
            lines.RemoveAt(0);
        }

        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var tail = lines.Skip(Math.Max(0, lines.Count - TailLines));
        return string.Join(Environment.NewLine, newestFirst ? tail.Reverse() : tail);
    }
}
