using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Services;

public class LlmService(
    ILlmProviderService providerService,
    ILogger<LlmService> logger
) : ILlmService
{
    public async Task<ChatResponse> CompleteAsync(
        int providerConfigId,
        string modelId,
        IEnumerable<ChatMessage> messages,
        LlmModelParameters? parameters = null,
        CancellationToken ct = default)
    {
        var config = await providerService.GetProviderAsync(providerConfigId, ct);
        if (config == null)
        {
            throw new InvalidOperationException($"Provider config with id {providerConfigId} not found");
        }

        if (!config.IsEnabled)
        {
            throw new InvalidOperationException($"Provider '{config.Name}' is disabled");
        }

        var client = providerService.CreateChatClient(config, modelId);

        var chatOptions = new ChatOptions();
        if (parameters != null)
        {
            chatOptions.Temperature = parameters.Temperature;
            chatOptions.MaxOutputTokens = parameters.MaxTokens;
            chatOptions.TopP = parameters.TopP;
        }

        return await client.GetResponseAsync(messages.ToList(), chatOptions, ct);
    }

    public Task<ChatResponse> CompleteWithDefaultAsync(
        IEnumerable<ChatMessage> messages,
        LlmModelParameters? parameters = null,
        CancellationToken ct = default)
    {
        // Default provider/model will be implemented when AiOptions is extended.
        // For now, this throws until configuration is set up.
        throw new InvalidOperationException(
            "Default provider/model not configured. Please configure AI options first.");
    }
}
