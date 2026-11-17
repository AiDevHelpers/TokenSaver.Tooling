using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TokenSaver.Vs.Extension.Services;

/// <summary>Members of the TokenSaver user group and the local accounts that could be added.</summary>
public sealed record AuthUsersInfo(string Group, IReadOnlyList<string> Members, IReadOnlyList<string> Candidates);

/// <summary>
/// Client side of the endpoint token (TokenSaver.MCP <c>EndpointAuthMiddleware</c>): reads the token file and calls
/// /api/auth/users behind the Settings "Users" card. Mirrors <c>auth.ts</c> in the VS Code extension.
/// </summary>
public static class AuthClient
{
    private static readonly Regex s_account = new(@"^([\w .\-$]{1,64}\\)?[\w .\-$]{1,64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Token file locations, most likely first: the service's ProgramData file, then the dev-run profile fallback.</summary>
    public static IReadOnlyList<string> DefaultTokenPaths(string? programData, string? localAppData)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(programData))
        {
            paths.Add(Path.Combine(programData, "TokenSaver", "auth-token"));
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            paths.Add(Path.Combine(localAppData, "TokenSaver", "auth-token"));
        }

        return paths;
    }

    /// <summary>
    /// Reads the endpoint token from <paramref name="configuredPath"/> if set, else the default locations. Null when no
    /// readable file holds a token (not installed, or this account is not in the TokenSaver group).
    /// </summary>
    public static string? ReadToken(string? configuredPath = null)
    {
        IEnumerable<string> candidates = string.IsNullOrWhiteSpace(configuredPath)
            ? DefaultTokenPaths(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            : [Environment.ExpandEnvironmentVariables(configuredPath.Trim())];

        foreach (var candidate in candidates)
        {
            try
            {
                var token = File.ReadAllText(candidate).Trim();
                if (token.Length >= 32)
                {
                    return token;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Missing or unreadable: try the next location.
            }
        }

        return null;
    }

    /// <summary>DOMAIN\name or name, no path or shell characters and no all-dot segments.</summary>
    public static bool IsValidAccount(string? account)
    {
        return !string.IsNullOrWhiteSpace(account)
            && s_account.IsMatch(account)
            && account.Split('\\').All(segment => segment.Trim('.', ' ').Length > 0);
    }

    public static async Task<AuthUsersInfo> ListAsync(int port, string token, CancellationToken ct = default)
    {
        using var http = NewClient(token);
        using var response = await http.GetAsync(Url(port, "/api/auth/users"), ct).ConfigureAwait(false);
        await EnsureOkAsync(response, ct).ConfigureAwait(false);
        var body = await response.Content.ReadFromJsonAsync<UsersBody>(ct).ConfigureAwait(false);
        return new AuthUsersInfo(body?.Group ?? "", body?.Members ?? [], body?.Candidates ?? []);
    }

    public static async Task AddAsync(int port, string token, string account, CancellationToken ct = default)
    {
        using var http = NewClient(token);
        using var response = await http.PostAsJsonAsync(Url(port, "/api/auth/users"), new { account }, ct).ConfigureAwait(false);
        await EnsureOkAsync(response, ct).ConfigureAwait(false);
    }

    public static async Task RemoveAsync(int port, string token, string account, CancellationToken ct = default)
    {
        using var http = NewClient(token);
        using var response = await http
            .DeleteAsync(Url(port, "/api/auth/users?account=" + Uri.EscapeDataString(account)), ct)
            .ConfigureAwait(false);
        await EnsureOkAsync(response, ct).ConfigureAwait(false);
    }

    private static Uri Url(int port, string pathAndQuery)
    {
        // 127.0.0.1, not localhost: the server binds loopback and rejects any other Host.
        return new Uri($"http://127.0.0.1:{port}{pathAndQuery}");
    }

    private static HttpClient NewClient(string token)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task EnsureOkAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("The server refused the token. Make sure this account is in the TokenSaver group, or reinstall.");
        }

        string? detail = null;
        try
        {
            detail = (await response.Content.ReadFromJsonAsync<ErrorBody>(ct).ConfigureAwait(false))?.Error;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            // Not JSON.
        }

        throw new InvalidOperationException($"Request failed ({(int)response.StatusCode}){(detail is null ? "" : ": " + detail)}");
    }

    // Created by the JSON deserializer, not by code.
#pragma warning disable CA1812
    private sealed record UsersBody(string? Group, List<string>? Members, List<string>? Candidates);

    private sealed record ErrorBody(string? Error);
#pragma warning restore CA1812
}
