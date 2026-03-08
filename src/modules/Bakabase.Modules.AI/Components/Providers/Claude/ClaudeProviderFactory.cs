using System.ClientModel;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Bakabase.Modules.AI.Components.Providers.Claude;

/// <summary>
/// Anthropic Claude provider. Uses OpenAI-compatible endpoint for now.
/// Can be switched to the native Anthropic SDK when IChatClient support is confirmed.
/// </summary>
public class ClaudeProviderFactory(ILogger<ClaudeProviderFactory> logger) : ILlmProviderFactory
{
    private const string DefaultClaudeEndpoint = "https://api.anthropic.com/v1/";

    public LlmProviderType ProviderType => LlmProviderType.Claude;
    public string DisplayName => "Claude";

    public LlmCapabilities DefaultCapabilities =>
        LlmCapabilities.Chat | LlmCapabilities.ToolCalling | LlmCapabilities.Vision |
        LlmCapabilities.Streaming | LlmCapabilities.JsonMode;

    public bool RequiresApiKey => true;
    public bool RequiresEndpoint => false;
    public string? DefaultEndpoint => DefaultClaudeEndpoint;

    public IChatClient CreateClient(LlmProviderConfigDbModel config, string modelId)
    {
        // Use OpenAI-compatible endpoint (Anthropic provides one)
        // When Anthropic C# SDK has stable IChatClient support, this can be replaced
        var endpoint = config.Endpoint ?? DefaultClaudeEndpoint;
        var client = new OpenAIClient(
            new ApiKeyCredential(config.ApiKey!),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
        return client.GetChatClient(modelId).AsIChatClient();
    }

    public Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(LlmProviderConfigDbModel config,
        CancellationToken ct = default)
    {
        // Anthropic does not expose a model listing API.
        // Return known models as static list.
        IReadOnlyList<LlmModelInfo> models =
        [
            new LlmModelInfo
            {
                ModelId = "claude-sonnet-4-20250514",
                DisplayName = "Claude Sonnet 4",
                Capabilities = DefaultCapabilities
            },
            new LlmModelInfo
            {
                ModelId = "claude-3-5-haiku-20241022",
                DisplayName = "Claude 3.5 Haiku",
                Capabilities = DefaultCapabilities
            },
            new LlmModelInfo
            {
                ModelId = "claude-3-5-sonnet-20241022",
                DisplayName = "Claude 3.5 Sonnet",
                Capabilities = DefaultCapabilities
            }
        ];
        return Task.FromResult(models);
    }

    public async Task<bool> TestConnectionAsync(LlmProviderConfigDbModel config, CancellationToken ct = default)
    {
        try
        {
            var endpoint = config.Endpoint ?? DefaultClaudeEndpoint;
            var client = new OpenAIClient(
                new ApiKeyCredential(config.ApiKey!),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

            var chatClient = client.GetChatClient("claude-3-5-haiku-20241022").AsIChatClient();
            await chatClient.GetResponseAsync("hi", cancellationToken: ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
