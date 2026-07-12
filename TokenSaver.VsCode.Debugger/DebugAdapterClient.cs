using TokenSaver.Common.Core.Contracts;

using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace TokenSaver.VsCode.Debugger;

/// <summary>
///     VS Code Debug Adapter client that communicates with the VS Code extension
///     to control debugging via the Debug Adapter Protocol (DAP).
///     The extension listens on a named pipe and forwards DAP commands.
/// </summary>
public sealed class DebugAdapterClient : IAsyncDisposable
{
    private readonly string _namedPipeName;
    private NamedPipeClientStream? _pipeClient;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private readonly object _lockObject = new();

    public DebugAdapterClient(string ideType = "VsCode")
    {
        // Named pipe path for the IDE extension to listen on
        _namedPipeName = $"TokenSaver_{ideType}_Debugger";
    }

    /// <summary>
    ///     Connect to the VS Code extension via named pipe.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _pipeClient = new NamedPipeClientStream(".", _namedPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await _pipeClient.ConnectAsync(5000, ct); // 5 second timeout
            _writer = new StreamWriter(_pipeClient) { AutoFlush = true };
            _reader = new StreamReader(_pipeClient);
        }
        catch (Exception ex)
        {
            _pipeClient?.Dispose();
            _pipeClient = null;
            throw new InvalidOperationException($"Failed to connect to debug adapter via named pipe '{_namedPipeName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Send a debug command to VS Code and receive the response.
    /// </summary>
    public async Task<DebugResponse> SendCommandAsync(DebugCommand command, CancellationToken ct = default)
    {
        if (_writer == null || _reader == null)
            throw new InvalidOperationException("Not connected to debug adapter");

        lock (_lockObject)
        {
            var request = new DebugRequest(
                command.ToString(),
                JsonSerializer.Serialize(new { }),
                Guid.NewGuid().ToString("N")[..12]);

            _writer.WriteLine(JsonSerializer.Serialize(request));
        }

        // Read response
        string? responseLine = await _reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            throw new InvalidOperationException("Debug adapter connection closed");
        }

        var response = JsonSerializer.Deserialize<DebugResponse>(responseLine)
            ?? throw new InvalidOperationException("Failed to parse debug adapter response");

        return response;
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer != null)
        {
            await _writer.FlushAsync();
            _writer.Dispose();
        }

        _reader?.Dispose();
        _pipeClient?.Dispose();
    }
}

public sealed record DebugRequest(
    string Command,
    string Payload,
    string Id);

public sealed record DebugResponse(
    bool Success,
    string Message,
    string? ResponseId = null,
    object? Data = null);
