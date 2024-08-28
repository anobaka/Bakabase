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

    public async Task DeleteAll(Expression<Func<Bakabase.Abstractions.Models.Db.EnhancementRecord, bool>>? exp)
    {
        await orm.RemoveAll(exp);
    }
}