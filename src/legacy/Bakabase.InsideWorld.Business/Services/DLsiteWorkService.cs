using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services;

public class DLsiteWorkService(
    FullMemoryCacheResourceService<BakabaseDbContext, DLsiteWorkDbModel, int> orm,
    IBOptions<DLsiteOptions> dlsiteOptions,
    ILogger<DLsiteWorkService> logger)
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

    public async Task SyncFromApi(Func<int, int, Task>? onProgress = null, CancellationToken ct = default)
    {
        var accounts = dlsiteOptions.Value.Accounts;
        if (accounts == null || accounts.Count == 0)
        {
            logger.LogWarning("No DLsite accounts configured, skipping sync");
            return;
        }

        // TODO: DLsite purchase list API is not yet implemented in DLsiteClient.
        // Once the purchase list parsing is available, implement the actual sync here.
        logger.LogWarning("DLsite sync from API is not yet fully implemented");

        if (onProgress != null)
        {
            await onProgress(100, 0);
        }
    }
}
