using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services;

public class DLsiteWorkService(FullMemoryCacheResourceService<BakabaseDbContext, DLsiteWorkDbModel, int> orm)
    : IDLsiteWorkService
{
    public async Task<List<DLsiteWorkDbModel>> GetAll()
    {
        return await orm.GetAll();
    }

    public async Task<DLsiteWorkDbModel?> GetByWorkId(string workId)
    {
        return await orm.GetFirstOrDefault(x => x.WorkId == workId);
    }

    public async Task<List<DLsiteWorkDbModel>> GetByWorkIds(IEnumerable<string> workIds)
    {
        var ids = workIds.ToHashSet();
        return await orm.GetAll(x => ids.Contains(x.WorkId));
    }

    public async Task AddOrUpdate(DLsiteWorkDbModel work)
    {
        var existing = await orm.GetFirstOrDefault(x => x.WorkId == work.WorkId);
        if (existing != null)
        {
            work.Id = existing.Id;
            work.UpdatedAt = DateTime.Now;
            await orm.Update(work);
        }
        else
        {
            work.CreatedAt = DateTime.Now;
            work.UpdatedAt = DateTime.Now;
            await orm.Add(work);
        }
    }

    public async Task AddOrUpdateRange(IEnumerable<DLsiteWorkDbModel> works)
    {
        foreach (var work in works)
        {
            await AddOrUpdate(work);
        }
    }

    public async Task DeleteByWorkId(string workId)
    {
        var existing = await orm.GetFirstOrDefault(x => x.WorkId == workId);
        if (existing != null)
        {
            await orm.RemoveByKey(existing.Id);
        }
    }
}
