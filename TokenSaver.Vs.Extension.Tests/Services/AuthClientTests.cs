using System;
using System.IO;

using TokenSaver.Vs.Extension.Services;

using Xunit;

namespace TokenSaver.Vs.Extension.Tests.Services;

public sealed class AuthClientTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vsauth-" + Guid.NewGuid().ToString("N"));

    public AuthClientTests()
    {
        _ = Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void DefaultTokenPaths_ProgramDataFirstThenLocalAppData()
    {
        var paths = AuthClient.DefaultTokenPaths(@"C:\PD", @"C:\LA");

        Assert.Equal([Path.Combine(@"C:\PD", "TokenSaver", "auth-token"), Path.Combine(@"C:\LA", "TokenSaver", "auth-token")], paths);
    }

    [Fact]
    public void ReadToken_UsesTheConfiguredPathAndTrims()
    {
        var file = Path.Combine(_dir, "token");
        File.WriteAllText(file, new string('a', 43) + "\r\n");

        Assert.Equal(new string('a', 43), AuthClient.ReadToken(file));
    }

    [Fact]
    public void ReadToken_MissingOrTooShort_IsNull()
    {
        var shortFile = Path.Combine(_dir, "short");
        File.WriteAllText(shortFile, "tiny");

        Assert.Null(AuthClient.ReadToken(Path.Combine(_dir, "nope")));
        Assert.Null(AuthClient.ReadToken(shortFile));
    }

    [Theory]
    [InlineData("alice", true)]
    [InlineData("PC\\alice", true)]
    [InlineData("first last", true)]
    [InlineData("svc$", true)]
    [InlineData("", false)]
    [InlineData("..\\x", false)]
    [InlineData("a/b", false)]
    [InlineData("a;rm -rf", false)]
    [InlineData("a\"b", false)]
    [InlineData("..", false)]
    public void IsValidAccount_MatchesTheServerRules(string account, bool expected)
    {
        Assert.Equal(expected, AuthClient.IsValidAccount(account));
    }
}
