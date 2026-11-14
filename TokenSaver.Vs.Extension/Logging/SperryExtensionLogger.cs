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

    // Kept alive for the life of the process: the logger writes through this factory's providers.
    private static ILoggerFactory? s_factory;

    private readonly ILogger _logger;

    public SperryExtensionLogger(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Creates a logger on the SperryDev "VisualStudio" channel at Information level.</summary>
    public static SperryExtensionLogger Create()
    {
        s_factory = AppLoggerFactory.Create(Channel, LogEventLevel.Information);
        return new SperryExtensionLogger(s_factory.CreateLogger("TokenSaver.Vs.Extension"));
    }

    public void LogInfo(string message)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("{Message}", message);
        }
    }

    public void LogError(string message)
    {
        if (_logger.IsEnabled(LogLevel.Error))
        {
            _logger.LogError("{Message}", message);
        }
    }

    public void LogException(Exception exception)
    {
        if (_logger.IsEnabled(LogLevel.Error))
        {
            _logger.LogError(exception, "{Message}", exception.Message);
        }
    }
}
