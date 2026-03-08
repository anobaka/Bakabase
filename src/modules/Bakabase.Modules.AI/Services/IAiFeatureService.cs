using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Services;

public interface IAiFeatureService
{
    Task<AiFeatureConfigDbModel?> GetConfigAsync(AiFeature feature, CancellationToken ct = default);
    Task<IReadOnlyList<AiFeatureConfigDbModel>> GetAllConfigsAsync(CancellationToken ct = default);
    Task<AiFeatureConfigDbModel> SaveConfigAsync(AiFeatureConfigDbModel config, CancellationToken ct = default);
    Task DeleteConfigAsync(AiFeature feature, CancellationToken ct = default);
}
