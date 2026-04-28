using System.ClientModel;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Bakabase.Modules.AI.Components.Providers.DashScope;

/// <summary>
/// Alibaba Cloud DashScope provider, using OpenAI-compatible endpoint.
/// </summary>
public class DashScopeProviderFactory(ILogger<DashScopeProviderFactory> logger) : ILlmProviderFactory
{
    private const string DefaultDashScopeEndpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1";

    public LlmProviderType ProviderType => LlmProviderType.DashScope;
    public string DisplayName => "阿里云百炼";

    public LlmCapabilities DefaultCapabilities =>
        LlmCapabilities.Chat | LlmCapabilities.ToolCalling | LlmCapabilities.Streaming | LlmCapabilities.JsonMode;

    public bool RequiresApiKey => true;
    public bool RequiresEndpoint => false;
    public string? DefaultEndpoint => DefaultDashScopeEndpoint;

    public IChatClient CreateClient(LlmProviderConfigDbModel config, string modelId)
    {
        var endpoint = config.Endpoint ?? DefaultDashScopeEndpoint;
        var client = new OpenAIClient(
            new ApiKeyCredential(config.ApiKey!),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
        return client.GetChatClient(modelId).AsIChatClient();
    }

    public async Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(LlmProviderConfigDbModel config,
        CancellationToken ct = default)
    {
        try
        {
            var endpoint = config.Endpoint ?? DefaultDashScopeEndpoint;
            var client = new OpenAIClient(
                new ApiKeyCredential(config.ApiKey!),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

            var models = await client.GetOpenAIModelClient().GetModelsAsync(ct);

            return models.Value
                .OrderBy(m => m.Id)
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
            logger.LogWarning(ex, "Failed to list models for DashScope provider {Name}", config.Name);
            return [];
        }
    }

    public async Task<bool> TestConnectionAsync(LlmProviderConfigDbModel config, CancellationToken ct = default)
    {
        try
        {
            var endpoint = config.Endpoint ?? DefaultDashScopeEndpoint;
            var client = new OpenAIClient(
                new ApiKeyCredential(config.ApiKey!),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

            // Send a minimal request to verify connectivity
            var chatClient = client.GetChatClient("qwen-turbo").AsIChatClient();
            await chatClient.GetResponseAsync("hi", cancellationToken: ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
