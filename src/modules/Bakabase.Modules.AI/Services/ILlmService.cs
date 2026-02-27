using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Services;

/// <summary>
/// Unified LLM invocation service. Wraps provider management and provides
/// a high-level API for calling LLM with proper provider/model resolution.
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// Call an LLM using explicit provider and model configuration.
    /// </summary>
    Task<ChatResponse> CompleteAsync(
        int providerConfigId,
        string modelId,
        IEnumerable<ChatMessage> messages,
        LlmModelParameters? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Call an LLM using the default provider and model from AiOptions.
    /// </summary>
    Task<ChatResponse> CompleteWithDefaultAsync(
        IEnumerable<ChatMessage> messages,
        LlmModelParameters? parameters = null,
        CancellationToken ct = default);
}
