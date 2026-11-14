using System;

using Microsoft.Extensions.Logging;

using Serilog.Events;

using SperryDev.Logger.Configuration;

namespace TokenSaver.Vs.Extension.Logging;

/// <summary>
/// <see cref="IExtensionLogger"/> backed by SperryDev.Logger (the same shared logger as TokenSaver.MCP
/// and SimpleModelServer). Output goes through the SperryDev channel configured by <see cref="Create"/>.
/// </summary>
public sealed class SperryExtensionLogger : IExtensionLogger
{
    private const string Channel = "VisualStudio";

    private readonly ILogger _logger;

    public SperryExtensionLogger(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Creates a logger on the SperryDev "VisualStudio" channel at Information level.</summary>
    public static SperryExtensionLogger Create()
    {
        var factory = AppLoggerFactory.Create(Channel, LogEventLevel.Information);
        return new SperryExtensionLogger(factory.CreateLogger("TokenSaver.Vs.Extension"));
    }

    public void LogInfo(string message) => _logger.LogInformation("{Message}", message);

    public void LogError(string message) => _logger.LogError("{Message}", message);

    public void LogException(Exception exception) => _logger.LogError(exception, "{Message}", exception.Message);
}
