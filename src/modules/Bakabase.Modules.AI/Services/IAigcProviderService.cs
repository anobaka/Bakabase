using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Models.Input;

namespace Bakabase.Modules.AI.Services;

public interface IAigcProviderService
{
    Task<IReadOnlyList<AigcProviderConfigDbModel>> GetAllAsync(CancellationToken ct = default);
    Task<AigcProviderConfigDbModel?> GetAsync(int id, CancellationToken ct = default);
    Task<AigcProviderConfigDbModel> AddAsync(AigcProviderConfigAddInputModel input, CancellationToken ct = default);
    Task<AigcProviderConfigDbModel> UpdateAsync(int id, AigcProviderConfigUpdateInputModel input, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(int id, CancellationToken ct = default);
    IReadOnlyList<AigcProviderKindInfo> GetProviderKinds();
}
