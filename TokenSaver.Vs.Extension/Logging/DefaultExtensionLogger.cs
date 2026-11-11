using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace TokenSaver.Vs.Extension.Logging;

/// <summary>Trace-based logger so log output shows up under DebugView / Visual Studio's own
/// diagnostics. Also keeps an in-memory copy of recent entries for test introspection.</summary>
public sealed class DefaultExtensionLogger : IExtensionLogger
{
    private readonly ConcurrentQueue<string> _entries = new();

    public void LogInfo(string message) => Record("INFO", message);

    public void LogError(string message) => Record("ERROR", message);

    public void LogException(Exception exception) => Record("ERROR", exception.ToString());

    public IReadOnlyList<string> Entries => [.. _entries];

    private void Record(string level, string message)
    {
        var entry = $"[TokenSaver] {level}: {message}";
        _entries.Enqueue(entry);
        if (level == "ERROR")
        {
            Trace.TraceError(entry);
        }
        else
        {
            Trace.TraceInformation(entry);
        }
    }
}
