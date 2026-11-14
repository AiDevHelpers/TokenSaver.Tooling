using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using TokenSaver.Vs.Extension.Logging;

namespace TokenSaver.Vs.Extension.Bridge;

/// <summary>
/// Named-pipe server for one Visual Studio instance. Each request is one JSON line:
/// <c>{"action":"...","args":{...}}</c>. Each reply is one JSON line: <c>{"ok":true,"result":{...}}</c> or
/// <c>{"ok":false,"error":"..."}</c>. The pipe name embeds the process id, so several instances run side by side.
/// </summary>
internal sealed class BridgeServer : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<string, JsonElement, Task<object>> _invoke;
    private readonly IExtensionLogger _logger;
    private readonly CancellationTokenSource _cts = new();

    public BridgeServer(string pipeName, Func<string, JsonElement, Task<object>> invoke, IExtensionLogger logger)
    {
        _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start() => _ = Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                await HandleAsync(server, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (IOException ex)
            {
                _logger.LogException(ex);
            }
        }
    }

    private async Task HandleAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        var reply = await ReplyAsync(line).ConfigureAwait(false);
        await writer.WriteLineAsync(reply).ConfigureAwait(false);
    }

    private async Task<string> ReplyAsync(string? line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line ?? string.Empty);
            var root = doc.RootElement;
            var action = root.GetProperty("action").GetString() ?? string.Empty;
            var args = root.TryGetProperty("args", out var a) ? a.Clone() : default;

            var result = await _invoke(action, args).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { ok = true, pid = Environment.ProcessId, result });
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return JsonSerializer.Serialize(new { ok = false, pid = Environment.ProcessId, error = ex.Message });
        }
    }

    public void Dispose() => _cts.Cancel();
}
