using System;

using SperryDev.Ide.Bridge;

namespace TokenSaver.Vs.Extension.Logging;

/// <summary>Adapts the extension's logger to the Ide-Bridge package's <see cref="IBridgeLogger"/>.</summary>
internal sealed class BridgeLogger(IExtensionLogger inner) : IBridgeLogger
{
    public void LogDebug(string message) => inner.LogDebug(message);

    public void LogInfo(string message) => inner.LogInfo(message);

    public void LogError(string message) => inner.LogError(message);

    public void LogException(Exception exception) => inner.LogException(exception);
}
