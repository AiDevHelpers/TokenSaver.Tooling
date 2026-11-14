using System;

namespace TokenSaver.Vs.Extension.Logging;

/// <summary>
/// Process-wide logger for the production VS extension entry points (package init and commands).
/// Created on first use so nothing is written to disk until the extension actually runs.
/// </summary>
internal static class ExtensionLog
{
    private static readonly Lazy<IExtensionLogger> Instance = new(SperryExtensionLogger.Create);

    public static IExtensionLogger Logger => Instance.Value;
}
