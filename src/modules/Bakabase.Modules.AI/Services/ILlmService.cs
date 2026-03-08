using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Services;

/// <summary>
/// Unified LLM invocation service with observation, caching, and quota management.
/// </summary>
public interface ILlmService
{
    Task<ChatResponse> CompleteAsync(
        int providerConfigId,
        string modelId,
        IEnumerable<ChatMessage> messages,
        LlmModelParameters? parameters = null,
        AiFeature? feature = null,
        CancellationToken ct = default);

    Task<ChatResponse> CompleteWithDefaultAsync(
        IEnumerable<ChatMessage> messages,
        LlmModelParameters? parameters = null,
        AiFeature? feature = null,
        CancellationToken ct = default);

    Task<ChatResponse> CompleteForFeatureAsync(
        AiFeature feature,
        IEnumerable<ChatMessage> messages,
        LlmModelParameters? parametersOverride = null,
        CancellationToken ct = default);
}
