using System;
using System.IO;
using System.Threading.Tasks;
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
            var logger = new DefaultExtensionLogger();
            var extension = new TokenSaverExtension(tempDir, logger);

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
        var logger = new DefaultExtensionLogger();
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
            var logger = new DefaultExtensionLogger();
            var extension = new TokenSaverExtension(tempDir, logger);

            await extension.InitializeAsync();

            // Check that we logged something
            var defaultLogger = (DefaultExtensionLogger)logger;
            Assert.NotEmpty(defaultLogger.GetEntries());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
