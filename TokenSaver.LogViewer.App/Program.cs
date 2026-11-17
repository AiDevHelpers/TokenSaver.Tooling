using System.Diagnostics;
using System.IO.Pipes;
using TokenSaver.LogViewer;
using TokenSaver.LogViewer.App;

string? pipeName = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--pipe")
    {
        pipeName = args[i + 1];
    }
}

if (string.IsNullOrWhiteSpace(pipeName))
{
    await Console.Error.WriteLineAsync("tokensaver-logview --pipe <name>");
    await Console.Error.WriteLineAsync("Opened by TokenSaver (LogWatch tool); it shows the log TokenSaver is watching.");
    return 2;
}

var screen = new Screen();
var quit = new CancellationTokenSource();
_ = Task.Run(() => screen.ReadKeysAsync(quit));

// Reconnects for a short while after the server goes away so a restart of it does not litter tabs, then exits
// with code 0 so Windows Terminal closes the tab.
DateTime lostAt = DateTime.MinValue;
while (!quit.IsCancellationRequested)
{
    try
    {
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000, quit.Token);
        using var reader = new StreamReader(client);
        await using var writer = new StreamWriter(client) { AutoFlush = true };
        await writer.WriteLineAsync(new ViewerMessage { Type = ViewerMessage.Ready, Pid = Environment.ProcessId }.ToLine());
        lostAt = DateTime.MinValue;
        screen.Status("connected");

        while (await reader.ReadLineAsync(quit.Token) is { } line)
        {
            if (!ViewerMessage.TryParse(line, out ViewerMessage? message) || message is null)
            {
                continue;
            }

            if (message.Type == ViewerMessage.Close)
            {
                return 0;
            }

            screen.Show(message);
        }
    }
    catch (OperationCanceledException)
    {
        break;
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
    {
        // Fall through to the wait below.
    }

    if (quit.IsCancellationRequested)
    {
        break;
    }

    if (lostAt == DateTime.MinValue)
    {
        lostAt = DateTime.UtcNow;
    }

    if (DateTime.UtcNow - lostAt > TimeSpan.FromSeconds(15))
    {
        return 0;
    }

    screen.Status("disconnected; waiting for TokenSaver");
    await Task.Delay(1000);
}

return 0;
