using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TokenSaver.Vs.Extension.Services;

/// <summary>
/// Talks to DraftModel.Loader's OpenAI-compatible endpoints (/v1/models, /v1/chat/completions,
/// /health). See DraftModel repo's docs/router-contract.md for the full HTTP contract.
/// </summary>
public sealed class HttpDraftModelClient : IDraftModelClient, IDisposable
{
    private static readonly DraftModelHealthStatus[] KnownStatuses =
    [
        DraftModelHealthStatus.Ok,
        DraftModelHealthStatus.WarmingUp,
        DraftModelHealthStatus.Degraded,
        DraftModelHealthStatus.Unhealthy,
    ];

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public HttpDraftModelClient(Uri baseUrl)
        : this(new HttpClient { BaseAddress = baseUrl }, ownsHttpClient: true)
    {
    }

    public HttpDraftModelClient(HttpClient httpClient, bool ownsHttpClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<DraftModelHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/health", cancellationToken).ConfigureAwait(false);
            var body = await response.Content
                .ReadFromJsonAsync<HealthBody>(cancellationToken)
                .ConfigureAwait(false);

            if (body?.Status is { } status && TryParseStatus(status, out var parsed))
            {
                return new DraftModelHealth(parsed);
            }

            return new DraftModelHealth(response.IsSuccessStatusCode
                ? DraftModelHealthStatus.Ok
                : DraftModelHealthStatus.Unhealthy);
        }
        catch
        {
            return new DraftModelHealth(DraftModelHealthStatus.Unreachable);
        }
    }

    public async Task<IReadOnlyList<DraftModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("/v1/models", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"DraftModel /v1/models returned {(int)response.StatusCode}");
        }

        var body = await response.Content
            .ReadFromJsonAsync<ModelsListBody>(cancellationToken)
            .ConfigureAwait(false);

        return (body?.Data ?? [])
            .Where(entry => !string.IsNullOrEmpty(entry.Id))
            .Select(entry => new DraftModelInfo(entry.Id!, entry.Id!))
            .ToList();
    }

    public async IAsyncEnumerable<DraftModelChatChunk> StreamChatAsync(
        IReadOnlyList<DraftModelChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestBody = new
        {
            messages = messages.Select(m => new { role = RoleToString(m.Role), content = m.Content }),
            stream = true,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(requestBody),
        };

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"DraftModel /v1/chat/completions returned {(int)response.StatusCode}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var frameLines = new List<string>();
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;

            if (line.Length == 0)
            {
                var chunk = ParseSseFrame(frameLines);
                frameLines.Clear();
                if (chunk is null) continue;
                yield return chunk;
                if (chunk.Done) yield break;
                continue;
            }

            frameLines.Add(line);
        }

        var trailing = ParseSseFrame(frameLines);
        if (trailing is not null) yield return trailing;
    }

    public int EstimateTokenCount(string text)
    {
        // No real tokenizer is exposed by the router; ~4 chars/token is the standard rough
        // estimate used when nothing better is available.
        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    private static DraftModelChatChunk? ParseSseFrame(List<string> lines)
    {
        var dataLine = lines.Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("data:", StringComparison.Ordinal));
        if (dataLine is null) return null;

        var payload = dataLine["data:".Length..].Trim();
        if (payload == "[DONE]")
        {
            return new DraftModelChatChunk(string.Empty, Done: true);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ChatCompletionChunk>(payload);
            var choice = parsed?.Choices?.FirstOrDefault();
            return new DraftModelChatChunk(
                choice?.Delta?.Content ?? string.Empty,
                Done: choice?.FinishReason is not null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string RoleToString(DraftModelChatRole role) => role switch
    {
        DraftModelChatRole.System => "system",
        DraftModelChatRole.Assistant => "assistant",
        _ => "user",
    };

    private static bool TryParseStatus(string raw, out DraftModelHealthStatus status)
    {
        foreach (var candidate in KnownStatuses)
        {
            if (string.Equals(RawStatus(candidate), raw, StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }

        status = DraftModelHealthStatus.Unreachable;
        return false;
    }

    private static string RawStatus(DraftModelHealthStatus status) => status switch
    {
        DraftModelHealthStatus.Ok => "ok",
        DraftModelHealthStatus.WarmingUp => "warming_up",
        DraftModelHealthStatus.Degraded => "degraded",
        DraftModelHealthStatus.Unhealthy => "unhealthy",
        _ => "unreachable",
    };

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed class HealthBody
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private sealed class ModelsListBody
    {
        [JsonPropertyName("data")]
        public List<ModelEntry>? Data { get; set; }
    }

    private sealed class ModelEntry
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private sealed class ChatCompletionChunk
    {
        [JsonPropertyName("choices")]
        public List<ChatCompletionChoice>? Choices { get; set; }
    }

    private sealed class ChatCompletionChoice
    {
        [JsonPropertyName("delta")]
        public ChatCompletionDelta? Delta { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private sealed class ChatCompletionDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
