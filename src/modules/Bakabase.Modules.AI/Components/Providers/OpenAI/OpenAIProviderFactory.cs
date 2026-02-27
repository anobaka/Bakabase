using System.ClientModel;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Bakabase.Modules.AI.Components.Providers.OpenAI;

public class OpenAIProviderFactory(ILogger<OpenAIProviderFactory> logger) : ILlmProviderFactory
{
    public LlmProviderType ProviderType => LlmProviderType.OpenAI;
    public string DisplayName => "OpenAI";

    public LlmCapabilities DefaultCapabilities =>
        LlmCapabilities.Chat | LlmCapabilities.ToolCalling | LlmCapabilities.Vision |
        LlmCapabilities.Streaming | LlmCapabilities.JsonMode;

    public bool RequiresApiKey => true;
    public bool RequiresEndpoint => false;
    public string? DefaultEndpoint => null;

    public IChatClient CreateClient(LlmProviderConfigDbModel config, string modelId)
    {
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrEmpty(config.Endpoint))
        {
            options.Endpoint = new Uri(config.Endpoint);
        }

        var client = new OpenAIClient(new ApiKeyCredential(config.ApiKey!), options);
        return client.GetChatClient(modelId).AsIChatClient();
    }

    public async Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(LlmProviderConfigDbModel config,
        CancellationToken ct = default)
    {
        try
        {
            var options = new OpenAIClientOptions();
            if (!string.IsNullOrEmpty(config.Endpoint))
            {
                options.Endpoint = new Uri(config.Endpoint);
            }

            var client = new OpenAIClient(new ApiKeyCredential(config.ApiKey!), options);
            var models = await client.GetOpenAIModelClient().GetModelsAsync(ct);

            return models.Value
                .Where(m => m.Id.Contains("gpt") || m.Id.Contains("o1") || m.Id.Contains("o3") || m.Id.Contains("chatgpt"))
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new LlmModelInfo
                {
                    ModelId = m.Id,
                    DisplayName = m.Id,
                    Capabilities = DefaultCapabilities
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list models for OpenAI provider {Name}", config.Name);
            return [];
        }
    }

    public async Task<bool> TestConnectionAsync(LlmProviderConfigDbModel config, CancellationToken ct = default)
    {
        try
        {
            var options = new OpenAIClientOptions();
            if (!string.IsNullOrEmpty(config.Endpoint))
            {
                options.Endpoint = new Uri(config.Endpoint);
            }

            var client = new OpenAIClient(new ApiKeyCredential(config.ApiKey!), options);
            await client.GetOpenAIModelClient().GetModelsAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
