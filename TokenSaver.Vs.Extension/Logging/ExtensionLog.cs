using System;
using System.Diagnostics;

namespace TokenSaver.Vs.Extension.Logging;

/// <summary>
/// Process-wide logger for the production VS extension entry points (package init and commands).
/// Created on first use so nothing is written to disk until the extension actually runs. If the
/// SperryDev log directory cannot be created, falls back to Trace so the package still loads.
/// </summary>
internal static class ExtensionLog
{
    private static readonly Lazy<IExtensionLogger> Instance = new(Create);

    public static IExtensionLogger Logger => Instance.Value;

    private static IExtensionLogger Create()
    {
        try
        {
            return SperryExtensionLogger.Create();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[TokenSaver] SperryDev logger unavailable, using Trace: {ex}");
            return new TraceFallbackLogger();
        }
    }

    private sealed class TraceFallbackLogger : IExtensionLogger
    {
        public void LogInfo(string message) => Trace.TraceInformation($"[TokenSaver] INFO: {message}");

        public void LogError(string message) => Trace.TraceError($"[TokenSaver] ERROR: {message}");

        public void LogException(Exception exception) => Trace.TraceError($"[TokenSaver] ERROR: {exception}");
    }
}
