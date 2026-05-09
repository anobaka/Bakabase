using Bakabase.Modules.AI.Models.Db;

namespace Bakabase.Modules.AI.Services;

/// <summary>
/// AIGC-specific operations on AI providers. CRUD lives on <see cref="IAiProviderService"/>;
/// this interface is for things only AIGC-capable providers can do.
/// </summary>
public interface IAigcProviderService
{
    /// <summary>Returns all enabled providers with the AIGC capability turned on.</summary>
    Task<IReadOnlyList<AiProviderDbModel>> GetEnabledAigcProvidersAsync(CancellationToken ct = default);
}
