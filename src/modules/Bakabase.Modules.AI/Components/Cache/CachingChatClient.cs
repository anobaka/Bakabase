using System.Security.Cryptography;
using System.Text.Json;
using Bakabase.Modules.AI.Models.Db;
using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Components.Cache;

public class CachingChatClient(
    IChatClient innerClient,
    ILlmCacheService cacheService,
    int providerConfigId,
    string modelId,
    LlmCacheRequestCoordinator requestCoordinator,
    int cacheTtlDays = 7
) : DelegatingChatClient(innerClient)
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
        var chatMessages = new List<ChatMessage>(messages);
        // Opaque provider options and executable tools cannot be represented by
        // their JSON alone. Avoid caching those requests rather than conflating them.
        if (options?.RawRepresentationFactory != null || options?.Tools is { Count: > 0 } ||
            chatMessages.Any(m => m.RawRepresentation != null || m.Contents.Any(c => c.RawRepresentation != null)))
            return await base.GetResponseAsync(chatMessages, options, cancellationToken);

        string cacheKey;
        try
        {
            cacheKey = ComputeCacheKey(chatMessages, options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return await base.GetResponseAsync(chatMessages, options, cancellationToken);
        }

        if (await ReadCachedAsync(cacheKey, cancellationToken) is { } cached) return cached;

        // Each waiter owns only its own cancellation token and DI scope. A
        // cancelled waiter cannot abort another caller's in-flight generation.
        using var lease = await requestCoordinator.AcquireAsync(cacheKey, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadCachedAsync(cacheKey, cancellationToken) is { } afterWaiting) return afterWaiting;

        var result = await base.GetResponseAsync(chatMessages, options, cancellationToken);
        if (result.FinishReason == ChatFinishReason.Length || !HasValidJsonWhenRequested(result, options)) return result;

        var entry = new LlmCallCacheEntryDbModel
        {
            CacheKey = cacheKey,
            ResponseJson = JsonSerializer.Serialize(result, JsonOptions),
            ProviderConfigId = providerConfigId,
            ModelId = modelId,
            CreatedAt = DateTime.Now,
            ExpiresAt = DateTime.Now.AddDays(cacheTtlDays)
        };
        await cacheService.SetAsync(entry, cancellationToken);

        return result;
    }

    private async Task<ChatResponse?> ReadCachedAsync(string key, CancellationToken ct)
    {
        var entry = await cacheService.GetAsync(key, ct);
        if (entry == null) return null;
        try
        {
            var response = JsonSerializer.Deserialize<ChatResponse>(entry.ResponseJson, JsonOptions);
            return response?.FinishReason == ChatFinishReason.Length ? null : response;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string ComputeCacheKey(IList<ChatMessage> messages, ChatOptions? options)
    {
        // The version isolates legacy entries that omitted response format and
        // reasoning. Serialize full options/content so future effective settings,
        // schemas, and non-text message parts cannot silently share an entry.
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { providerConfigId, modelId, options, messages }, JsonOptions);
        return "v2:" + Convert.ToHexString(SHA256.HashData(payload));
    }

    private static bool HasValidJsonWhenRequested(ChatResponse response, ChatOptions? options)
    {
        if (options?.ResponseFormat is not ChatResponseFormatJson) return true;
        try
        {
            using var json = JsonDocument.Parse(response.Text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
