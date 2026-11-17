namespace TokenSaver.Vs.Extension.Logging;

/// <summary>
/// Minimal logging seam for the extension. Kept local (rather than depending on
/// TokenSaver.Client.Helpers) because this project intentionally stays a thin, self-contained
/// client — see the "why" comment at the top of TokenSaver.Vs.Extension.csproj.
/// </summary>
public interface IExtensionLogger
{
    /// <summary>Detail that is only written when "Extension debug logging" is on.</summary>
    void LogDebug(string message);

    void LogInfo(string message);

    void LogError(string message);

    void LogException(Exception exception);
}
