using Bakabase.Modules.AI.Models.Db;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.AI.Services;

public class AigcProviderService<TDbContext>(
    ResourceService<TDbContext, AiProviderDbModel, int> orm
) : IAigcProviderService where TDbContext : DbContext
{
    public async Task<IReadOnlyList<AiProviderDbModel>> GetEnabledAigcProvidersAsync(CancellationToken ct = default) =>
        await orm.GetAll(p => p.IsEnabled && p.AigcEnabled);
}
