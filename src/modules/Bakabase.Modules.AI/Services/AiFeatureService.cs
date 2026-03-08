using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.AI.Services;

public class AiFeatureService<TDbContext>(
    ResourceService<TDbContext, AiFeatureConfigDbModel, int> orm
) : IAiFeatureService where TDbContext : DbContext
{
    public async Task<AiFeatureConfigDbModel?> GetConfigAsync(AiFeature feature, CancellationToken ct = default)
    {
        return await orm.DbContext.Set<AiFeatureConfigDbModel>()
            .FirstOrDefaultAsync(c => c.Feature == feature, ct);
    }

    public async Task<IReadOnlyList<AiFeatureConfigDbModel>> GetAllConfigsAsync(CancellationToken ct = default)
    {
        return await orm.GetAll();
    }

    public async Task<AiFeatureConfigDbModel> SaveConfigAsync(AiFeatureConfigDbModel config,
        CancellationToken ct = default)
    {
        var existing = await orm.DbContext.Set<AiFeatureConfigDbModel>()
            .FirstOrDefaultAsync(c => c.Feature == config.Feature, ct);

        if (existing != null)
        {
            existing.UseDefault = config.UseDefault;
            existing.ProviderConfigId = config.ProviderConfigId;
            existing.ModelId = config.ModelId;
            existing.Temperature = config.Temperature;
            existing.MaxTokens = config.MaxTokens;
            existing.TopP = config.TopP;
            await orm.DbContext.SaveChangesAsync(ct);
            return existing;
        }

        var result = await orm.Add(config);
        return result.Data!;
    }

    public async Task DeleteConfigAsync(AiFeature feature, CancellationToken ct = default)
    {
        var existing = await orm.DbContext.Set<AiFeatureConfigDbModel>()
            .FirstOrDefaultAsync(c => c.Feature == feature, ct);
        if (existing != null)
        {
            orm.DbContext.Set<AiFeatureConfigDbModel>().Remove(existing);
            await orm.DbContext.SaveChangesAsync(ct);
        }
    }
}
