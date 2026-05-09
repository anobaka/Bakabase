using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Models.Input;

namespace Bakabase.Modules.AI.Services;

/// <summary>
/// Unified CRUD over <see cref="AiProviderDbModel"/>. A single record may expose
/// LLM and/or AIGC capability based on its kind and the per-capability enable flags.
/// Capability-specific operations (chat client creation, AIGC invocation) live on
/// <see cref="ILlmProviderService"/> and <see cref="IAigcProviderService"/>.
/// </summary>
public interface IAiProviderService
{
    Task<IReadOnlyList<AiProviderDbModel>> GetAllAsync(CancellationToken ct = default);
    Task<AiProviderDbModel?> GetAsync(int id, CancellationToken ct = default);
    Task<AiProviderDbModel> AddAsync(AiProviderAddInputModel input, CancellationToken ct = default);
    Task<AiProviderDbModel> UpdateAsync(int id, AiProviderUpdateInputModel input, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Tests every capability the provider currently has enabled.</summary>
    Task<AiProviderTestResult> TestAsync(int id, CancellationToken ct = default);

    IReadOnlyList<AiProviderKindInfo> GetKinds();
}
