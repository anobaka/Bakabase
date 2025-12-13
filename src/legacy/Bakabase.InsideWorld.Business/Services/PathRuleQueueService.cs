using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Services;

public class PathRuleQueueService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, PathRuleQueueItemDbModel, int> orm,
    IServiceProvider serviceProvider
) : ScopedService(serviceProvider), IPathRuleQueueService where TDbContext : DbContext
{
    public async Task<List<PathRuleQueueItem>> GetAll(Expression<Func<PathRuleQueueItemDbModel, bool>>? filter = null)
    {
        var dbModels = filter != null
            ? await orm.GetAll(filter)
            : await orm.GetAll();

        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<List<PathRuleQueueItem>> GetPendingItems(int batchSize = 100)
    {
        var dbModels = await orm.GetAll(q => q.Status == RuleQueueStatus.Pending);
        return dbModels
            .OrderBy(q => q.CreateDt)
            .Take(batchSize)
            .Select(d => d.ToDomainModel())
            .ToList();
    }

    public async Task<PathRuleQueueItem> Enqueue(PathRuleQueueItem item)
    {
        item.Status = RuleQueueStatus.Pending;
        item.CreateDt = DateTime.UtcNow;

        var dbModel = item.ToDbModel();
        await orm.Add(dbModel);
        orm.DbContext.Detach(dbModel);
        item.Id = dbModel.Id;
        return item;
    }

    public async Task EnqueueRange(IEnumerable<PathRuleQueueItem> items)
    {
        var now = DateTime.UtcNow;
        var dbModels = items.Select(item =>
        {
            item.Status = RuleQueueStatus.Pending;
            item.CreateDt = now;
            return item.ToDbModel();
        }).ToList();

        await orm.AddRange(dbModels);
    }

    public async Task UpdateStatus(int id, RuleQueueStatus status, string? error = null)
    {
        var dbModel = await orm.GetByKey(id);
        if (dbModel != null)
        {
            dbModel.Status = status;
            dbModel.Error = error;
            await orm.Update(dbModel);
        }
    }

    public async Task MarkAsProcessing(int id)
    {
        await UpdateStatus(id, RuleQueueStatus.Processing);
    }

    public async Task MarkAsCompleted(int id)
    {
        await UpdateStatus(id, RuleQueueStatus.Completed);
    }

    public async Task MarkAsFailed(int id, string error)
    {
        await UpdateStatus(id, RuleQueueStatus.Failed, error);
    }

    public async Task DeleteCompletedItems()
    {
        var completedItems = await orm.GetAll(q => q.Status == RuleQueueStatus.Completed);
        if (completedItems.Any())
        {
            await orm.RemoveRange(completedItems);
        }
    }

    public async Task DeleteAll()
    {
        var allItems = await orm.GetAll();
        if (allItems.Any())
        {
            await orm.RemoveRange(allItems);
        }
    }

    public async Task<QueueStatistics> GetStatistics()
    {
        var allItems = await orm.GetAll();

        return new QueueStatistics
        {
            PendingCount = allItems.Count(q => q.Status == RuleQueueStatus.Pending),
            ProcessingCount = allItems.Count(q => q.Status == RuleQueueStatus.Processing),
            CompletedCount = allItems.Count(q => q.Status == RuleQueueStatus.Completed),
            FailedCount = allItems.Count(q => q.Status == RuleQueueStatus.Failed)
        };
    }
}
