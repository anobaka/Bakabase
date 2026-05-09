using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Services;

/// <summary>
/// LLM-specific operations on AI providers. CRUD lives on <see cref="IAiProviderService"/>;
/// this interface is for things only LLM-capable providers can do.
/// </summary>
public interface ILlmProviderService
{
    /// <summary>Returns all enabled providers with the LLM capability turned on.</summary>
    Task<IReadOnlyList<AiProviderDbModel>> GetEnabledLlmProvidersAsync(CancellationToken ct = default);

    Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(int providerId, CancellationToken ct = default);

    IChatClient CreateChatClient(AiProviderDbModel config, string modelId);
}
