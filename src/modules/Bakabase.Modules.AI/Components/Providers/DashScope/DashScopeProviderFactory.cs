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
        // DashScope does not expose a standard model listing endpoint.
        // Return commonly used models as static list.
        await Task.CompletedTask;
        return
        [
            new LlmModelInfo
            {
                ModelId = "qwen-max",
                DisplayName = "Qwen Max",
                Capabilities = DefaultCapabilities | LlmCapabilities.Vision
            },
            new LlmModelInfo
            {
                ModelId = "qwen-plus",
                DisplayName = "Qwen Plus",
                Capabilities = DefaultCapabilities
            },
            new LlmModelInfo
            {
                ModelId = "qwen-turbo",
                DisplayName = "Qwen Turbo",
                Capabilities = DefaultCapabilities
            },
            new LlmModelInfo
            {
                ModelId = "qwen-long",
                DisplayName = "Qwen Long",
                Capabilities = DefaultCapabilities
            }
        ];
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
