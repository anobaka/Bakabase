using System.ClientModel;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Bakabase.Modules.AI.Components.Providers.Gemini;

/// <summary>
/// Google Gemini provider. Uses OpenAI-compatible endpoint.
/// Can be enhanced with Google_GenerativeAI.Microsoft package for native support later.
/// </summary>
public class GeminiProviderFactory(ILogger<GeminiProviderFactory> logger) : ILlmProviderFactory
{
    private const string DefaultGeminiEndpoint = "https://generativelanguage.googleapis.com/v1beta/openai/";

    public LlmProviderType ProviderType => LlmProviderType.Gemini;
    public string DisplayName => "Google Gemini";

    public LlmCapabilities DefaultCapabilities =>
        LlmCapabilities.Chat | LlmCapabilities.ToolCalling | LlmCapabilities.Vision |
        LlmCapabilities.Streaming | LlmCapabilities.JsonMode;

    public bool RequiresApiKey => true;
    public bool RequiresEndpoint => false;
    public string? DefaultEndpoint => DefaultGeminiEndpoint;

    public IChatClient CreateClient(LlmProviderConfigDbModel config, string modelId)
    {
        var endpoint = config.Endpoint ?? DefaultGeminiEndpoint;
        var client = new OpenAIClient(
            new ApiKeyCredential(config.ApiKey!),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
        return client.GetChatClient(modelId).AsIChatClient();
    }

    public Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(LlmProviderConfigDbModel config,
        CancellationToken ct = default)
    {
        IReadOnlyList<LlmModelInfo> models =
        [
            new LlmModelInfo
            {
                ModelId = "gemini-2.0-flash",
                DisplayName = "Gemini 2.0 Flash",
                Capabilities = DefaultCapabilities
            },
            new LlmModelInfo
            {
                ModelId = "gemini-2.0-flash-lite",
                DisplayName = "Gemini 2.0 Flash Lite",
                Capabilities = LlmCapabilities.Chat | LlmCapabilities.Streaming
            },
            new LlmModelInfo
            {
                ModelId = "gemini-1.5-pro",
                DisplayName = "Gemini 1.5 Pro",
                Capabilities = DefaultCapabilities
            },
            new LlmModelInfo
            {
                ModelId = "gemini-1.5-flash",
                DisplayName = "Gemini 1.5 Flash",
                Capabilities = DefaultCapabilities
            }
        ];
        return Task.FromResult(models);
    }

    public async Task<bool> TestConnectionAsync(LlmProviderConfigDbModel config, CancellationToken ct = default)
    {
        try
        {
            var endpoint = config.Endpoint ?? DefaultGeminiEndpoint;
            var client = new OpenAIClient(
                new ApiKeyCredential(config.ApiKey!),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

            var chatClient = client.GetChatClient("gemini-2.0-flash").AsIChatClient();
            await chatClient.GetResponseAsync("hi", cancellationToken: ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
