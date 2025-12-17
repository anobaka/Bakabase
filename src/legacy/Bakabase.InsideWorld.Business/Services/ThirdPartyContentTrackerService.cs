using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>
/// 第三方内容追踪服务实现
/// </summary>
public class ThirdPartyContentTrackerService(
    FullMemoryCacheResourceService<BakabaseDbContext, ThirdPartyContentTrackerDbModel, int> orm,
    BakabaseDbContext dbContext)
    : IThirdPartyContentTrackerService
{
    public async Task<List<ThirdPartyContentTrackerStatusViewModel>> QueryContentStatus(
        string domainKey,
        string? filter,
        List<string> contentIds)
    {
        if (contentIds == null || contentIds.Count == 0)
        {
            return new List<ThirdPartyContentTrackerStatusViewModel>();
        }

        // 查询数据库中已存在的记录
        var existingRecords = await dbContext.ThirdPartyContentTrackers
            .Where(t => t.DomainKey == domainKey &&
                       t.Filter == filter &&
                       contentIds.Contains(t.ContentId))
            .ToListAsync();

        var existingDict = existingRecords.ToDictionary(r => r.ContentId);

        // 构建返回结果
        var result = contentIds.Select(contentId =>
        {
            if (existingDict.TryGetValue(contentId, out var record))
            {
                return new ThirdPartyContentTrackerStatusViewModel
                {
                    ContentId = contentId,
                    UpdatedAt = record.UpdatedAt,
                    ViewedAt = record.ViewedAt
                };
            }
            else
            {
                return new ThirdPartyContentTrackerStatusViewModel
                {
                    ContentId = contentId,
                    UpdatedAt = null,
                    ViewedAt = null
                };
            }
        }).ToList();

        return result;
    }

    public async Task MarkContentAsViewed(
        string domainKey,
        string? filter,
        List<(string contentId, DateTime? updatedAt)> contentItems)
    {
        if (contentItems == null || contentItems.Count == 0)
        {
            return;
        }

        var contentIds = contentItems.Select(c => c.contentId).ToList();

        // 查询已存在的记录
        var existingRecords = await dbContext.ThirdPartyContentTrackers
            .Where(t => t.DomainKey == domainKey &&
                       t.Filter == filter &&
                       contentIds.Contains(t.ContentId))
            .ToListAsync();

        var existingDict = existingRecords.ToDictionary(r => r.ContentId);
        var now = DateTime.Now;

        foreach (var (contentId, updatedAt) in contentItems)
        {
            if (existingDict.TryGetValue(contentId, out var existing))
            {
                // 更新已有记录
                existing.ViewedAt = now;
                if (updatedAt.HasValue)
                {
                    existing.UpdatedAt = updatedAt;
                }
            }
            else
            {
                // 创建新记录
                var newRecord = new ThirdPartyContentTrackerDbModel
                {
                    DomainKey = domainKey,
                    Filter = filter,
                    ContentId = contentId,
                    UpdatedAt = updatedAt,
                    ViewedAt = now.ToUniversalTime(),
                    CreatedAt = now.ToUniversalTime()
                };
                await dbContext.ThirdPartyContentTrackers.AddAsync(newRecord);
            }
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task<ThirdPartyContentTrackerNearestViewModel?> FindNearestViewedContent(
        string domainKey,
        string? filter,
        string targetContentId)
    {
        // 查找目标内容的记录
        var targetRecord = await dbContext.ThirdPartyContentTrackers
            .FirstOrDefaultAsync(t => t.DomainKey == domainKey &&
                                     t.Filter == filter &&
                                     t.ContentId == targetContentId);

        if (targetRecord == null)
        {
            // 如果目标内容不存在，返回最近查看的任意内容
            var latestViewed = await dbContext.ThirdPartyContentTrackers
                .Where(t => t.DomainKey == domainKey && t.Filter == filter)
                .OrderByDescending(t => t.ViewedAt)
                .FirstOrDefaultAsync();

            if (latestViewed == null)
            {
                return null;
            }

            return new ThirdPartyContentTrackerNearestViewModel
            {
                ContentId = latestViewed.ContentId,
                ViewedAt = latestViewed.ViewedAt
            };
        }

        // 查找在目标内容之前查看的、离目标最近的内容
        // 这里使用 ID 作为排序依据（假设 ID 是递增的，代表内容的时间顺序）
        // 如果需要更复杂的排序逻辑，可能需要额外的字段或在客户端处理
        var nearestBefore = await dbContext.ThirdPartyContentTrackers
            .Where(t => t.DomainKey == domainKey &&
                       t.Filter == filter &&
                       string.Compare(t.ContentId, targetContentId, StringComparison.Ordinal) < 0)
            .OrderByDescending(t => t.ContentId)
            .FirstOrDefaultAsync();

        if (nearestBefore == null)
        {
            return null;
        }

        return new ThirdPartyContentTrackerNearestViewModel
        {
            ContentId = nearestBefore.ContentId,
            ViewedAt = nearestBefore.ViewedAt
        };
    }
}
