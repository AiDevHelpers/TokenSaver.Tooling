using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TokenSaver.Vs.Extension.Logging;
using Xunit;

namespace TokenSaver.Vs.Extension.Tests;

public sealed class TokenSaverExtensionTests
{
    [Fact]
    public void Constructor_WithValidArgs_Succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var logger = new SperryExtensionLogger(new CapturingLogger());
            using var extension = new TokenSaverExtension(tempDir, logger);

            Assert.NotNull(extension);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Constructor_WithNullWorkspaceRoot_Throws()
    {
        var logger = new SperryExtensionLogger(new CapturingLogger());
        Assert.Throws<ArgumentNullException>(() => new TokenSaverExtension(null!, logger));
    }

    [Fact]
    public void Constructor_WithNullLogger_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Assert.Throws<ArgumentNullException>(() => new TokenSaverExtension(tempDir, null!));
    }

    [Fact]
    public async Task InitializeAsync_Succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var captured = new CapturingLogger();
            using var extension = new TokenSaverExtension(tempDir, new SperryExtensionLogger(captured));

            await extension.InitializeAsync();

            // Check that we logged something
            Assert.NotEmpty(captured.Messages);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>Records every formatted message so tests can assert on what was logged.</summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
