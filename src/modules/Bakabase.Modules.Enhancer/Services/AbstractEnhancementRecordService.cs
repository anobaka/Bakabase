using System.Linq.Expressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Enhancer.Services;

public abstract class AbstractEnhancementRecordService<TDbContext>(
    ResourceService<TDbContext, Bakabase.Abstractions.Models.Db.EnhancementRecord, int> orm)
    : IEnhancementRecordService where TDbContext : DbContext
{
    public async Task<List<EnhancementRecord>> GetAll(
        Expression<Func<Bakabase.Abstractions.Models.Db.EnhancementRecord, bool>>? exp)
    {
        var dbData = await orm.GetAll(exp);
        return dbData.Select(d => d.ToDomainModel()!).ToList();
    }

    public async Task Add(EnhancementRecord record)
    {
        await orm.Add(record.ToDbModel());
    }

    public async Task Update(EnhancementRecord record)
    {
        await orm.Update(record.ToDbModel());
    }

    public async Task Update(IEnumerable<EnhancementRecord> records)
    {
        await orm.UpdateRange(records.Select(r => r.ToDbModel()));
    }

    public async Task DeleteAll(Expression<Func<Bakabase.Abstractions.Models.Db.EnhancementRecord, bool>>? exp)
    {
        await orm.RemoveAll(exp);
    }

    public async Task DeleteByResourceAndEnhancers(Dictionary<int, HashSet<int>> resourceIdsAndEnhancerIds)
    {
        var resourceIds = resourceIdsAndEnhancerIds.Keys.ToList();
        var data = await orm.GetAll(x => resourceIds.Contains(x.ResourceId));
        var recordsToDelete = data.Where(x =>
            resourceIdsAndEnhancerIds.TryGetValue(x.ResourceId, out var enhancerIds) &&
            enhancerIds.Contains(x.EnhancerId)).ToList();
        if (recordsToDelete.Any())
        {
            await orm.RemoveRange(recordsToDelete);
        }
    }
}