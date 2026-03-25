using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.AI.Models.Db;
using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Components.Cache;

public class CachingChatClient(
    IChatClient innerClient,
    ILlmCacheService cacheService,
    int providerConfigId,
    string modelId,
    int cacheTtlDays = 7
) : DelegatingChatClient(innerClient)
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
        var chatMessages = new List<ChatMessage>(messages);
        var cacheKey = ComputeCacheKey(chatMessages, options);

        var cached = await cacheService.GetAsync(cacheKey, cancellationToken);
        if (cached != null)
        {
            var response = JsonSerializer.Deserialize<ChatResponse>(cached.ResponseJson, JsonOptions);
            if (response != null)
            {
                return response;
            }
        }

        var result = await base.GetResponseAsync(chatMessages, options, cancellationToken);

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

    private string ComputeCacheKey(IList<ChatMessage> messages, ChatOptions? options)
    {
        var sb = new StringBuilder();
        sb.Append(providerConfigId);
        sb.Append('|');
        sb.Append(modelId);
        sb.Append('|');
        sb.Append(options?.Temperature);
        sb.Append('|');
        sb.Append(options?.MaxOutputTokens);
        sb.Append('|');
        sb.Append(options?.TopP);
        sb.Append('|');

        foreach (var msg in messages)
        {
            sb.Append(msg.Role);
            sb.Append(':');
            sb.Append(msg.Text);
            sb.Append(';');
        }

        if (options?.Tools is { Count: > 0 })
        {
            foreach (var tool in options.Tools)
            {
                sb.Append("tool:");
                sb.Append(tool.Name);
                sb.Append(';');
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash);
    }
}
