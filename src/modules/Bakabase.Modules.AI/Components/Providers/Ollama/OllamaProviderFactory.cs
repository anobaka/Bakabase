using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;

namespace Bakabase.Modules.AI.Components.Providers.Ollama;

public class OllamaProviderFactory(ILogger<OllamaProviderFactory> logger) : ILlmProviderFactory
{
    public LlmProviderType ProviderType => LlmProviderType.Ollama;
    public string DisplayName => "Ollama";

    public LlmCapabilities DefaultCapabilities =>
        LlmCapabilities.Chat | LlmCapabilities.Streaming | LlmCapabilities.Vision;

    public bool RequiresApiKey => false;
    public bool RequiresEndpoint => true;
    public string? DefaultEndpoint => "http://localhost:11434";

    public IChatClient CreateClient(LlmProviderConfigDbModel config, string modelId)
    {
        var endpoint = config.Endpoint ?? DefaultEndpoint!;
        var client = new OllamaApiClient(endpoint, modelId);
        return client;
    }

    public async Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(LlmProviderConfigDbModel config,
        CancellationToken ct = default)
    {
        try
        {
            var endpoint = config.Endpoint ?? DefaultEndpoint!;
            var client = new OllamaApiClient(endpoint);
            var models = await client.ListLocalModelsAsync(ct);

            return models
                .Select(m => new LlmModelInfo
                {
                    ModelId = m.Name,
                    DisplayName = m.Name,
                    Capabilities = DefaultCapabilities
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list models for Ollama provider {Name}", config.Name);
            return [];
        }
    }

    public async Task<bool> TestConnectionAsync(LlmProviderConfigDbModel config, CancellationToken ct = default)
    {
        try
        {
            var endpoint = config.Endpoint ?? DefaultEndpoint!;
            var client = new OllamaApiClient(endpoint);
            await client.ListLocalModelsAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
