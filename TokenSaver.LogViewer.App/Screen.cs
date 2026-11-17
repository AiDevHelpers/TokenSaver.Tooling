using TokenSaver.LogViewer;

namespace TokenSaver.LogViewer.App;

/// <summary>
/// Draws the stream. Entries the AI's filter did not match are dimmed; <c>f</c> hides them, so the user can see what the
/// filter left out. <c>p</c> pauses (entries still collect), <c>c</c> clears, <c>q</c> quits.
/// </summary>
internal sealed class Screen
{
    private const int MaxHistory = 5000;

    private readonly object _lock = new();
    private readonly List<ViewerMessage> _history = new();
    private string? _header;
    private bool _filteredOnly;
    private bool _paused;

    public void Show(ViewerMessage message)
    {
        lock (_lock)
        {
            if (message.Type == ViewerMessage.Hello)
            {
                _header = $"watching {message.Channel}" + (message.Filter is { Length: > 0 } f ? $"   AI filter: {f}" : string.Empty);
                if (!Console.IsOutputRedirected && message.Text is { Length: > 0 } title)
                {
                    Console.Title = title;
                }

                _history.Clear();
                Redraw();
                return;
            }

            _history.Add(message);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveRange(0, _history.Count - MaxHistory);
            }

            if (!_paused)
            {
                Print(message);
            }
        }
    }

    public void Status(string text)
    {
        lock (_lock)
        {
            WriteColored($"-- {text}", ConsoleColor.DarkCyan);
        }
    }

    public async Task ReadKeysAsync(CancellationTokenSource quit)
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        while (!quit.IsCancellationRequested)
        {
            if (!Console.KeyAvailable)
            {
                await Task.Delay(100);
                continue;
            }

            switch (char.ToLowerInvariant(Console.ReadKey(intercept: true).KeyChar))
            {
                case 'q':
                    await quit.CancelAsync();
                    return;
                case 'f':
                    lock (_lock)
                    {
                        _filteredOnly = !_filteredOnly;
                        Redraw();
                    }

                    break;
                case 'p':
                    lock (_lock)
                    {
                        _paused = !_paused;
                        if (_paused)
                        {
                            WriteColored("-- paused; entries keep collecting (p resumes)", ConsoleColor.Yellow);
                        }
                        else
                        {
                            Redraw();
                        }
                    }

                    break;
                case 'c':
                    lock (_lock)
                    {
                        _history.Clear();
                        Redraw();
                    }

                    break;
            }
        }
    }

    private void Redraw()
    {
        if (!Console.IsOutputRedirected)
        {
            Console.Clear();
        }

        if (_header is not null)
        {
            WriteColored(_header, ConsoleColor.Cyan);
        }

        WriteColored(
            $"keys: f = {(_filteredOnly ? "show everything" : "only what the AI filter matched")}   p = pause   c = clear   q = quit",
            ConsoleColor.DarkGray);
        foreach (ViewerMessage message in _history)
        {
            Print(message);
        }

    }

    private void Print(ViewerMessage message)
    {
        if (message.Type == ViewerMessage.Note)
        {
            WriteColored($"-- {message.Text}", ConsoleColor.DarkCyan);
            return;
        }

        bool matched = message.Matched != false;
        if (!matched && _filteredOnly)
        {
            return;
        }

        string time = message.Ts is { } ts ? ts.ToLocalTime().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) : "            ";
        string level = (message.Severity ?? "Unknown").ToUpperInvariant();
        string line = $"{time} {Short(level),-4} {(message.Category is { Length: > 0 } c ? c + ": " : string.Empty)}{message.Text}";
        WriteColored(line, matched ? ColorOf(level) : ConsoleColor.DarkGray);
    }

    private static string Short(string level) => level switch
    {
        "INFORMATION" => "INFO",
        "WARNING" => "WARN",
        "CRITICAL" => "CRIT",
        "TRACE" => "TRCE",
        "DEBUG" => "DBUG",
        "UNKNOWN" => "    ",
        _ => level[..Math.Min(4, level.Length)],
    };

    private static ConsoleColor ColorOf(string level) => level switch
    {
        "CRITICAL" => ConsoleColor.Magenta,
        "ERROR" => ConsoleColor.Red,
        "WARNING" => ConsoleColor.Yellow,
        "INFORMATION" => ConsoleColor.Gray,
        _ => ConsoleColor.DarkGray,
    };

    private static void WriteColored(string text, ConsoleColor color)
    {
        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}
