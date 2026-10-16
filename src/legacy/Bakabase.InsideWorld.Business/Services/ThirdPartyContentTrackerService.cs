using System;
using System.Collections.Generic;
using System.Globalization;
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

    public async Task<ThirdPartyContentTrackerPageBoundariesViewModel> FindPageBoundaries(
        string domainKey,
        string? filter,
        DateTime oldestUpdatedAt,
        DateTime newestUpdatedAt,
        IReadOnlyCollection<string> excludeContentIds)
    {
        if (oldestUpdatedAt.Kind != DateTimeKind.Utc || newestUpdatedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Page content times must use UTC.");
        if (oldestUpdatedAt > newestUpdatedAt)
            throw new ArgumentException("The oldest content time must not follow the newest content time.");
        var records = QueryHistoryRecords(domainKey, filter, excludeContentIds);

        var older = await records.Where(t => t.UpdatedAt <= oldestUpdatedAt)
            .OrderByDescending(t => t.UpdatedAt).ThenBy(t => t.ContentId).ThenBy(t => t.Id)
            .Select(t => new {t.ContentId, UpdatedAt = t.UpdatedAt!.Value})
            .FirstOrDefaultAsync();
        var newer = await records.Where(t => t.UpdatedAt >= newestUpdatedAt)
            .OrderBy(t => t.UpdatedAt).ThenBy(t => t.ContentId).ThenBy(t => t.Id)
            .Select(t => new {t.ContentId, UpdatedAt = t.UpdatedAt!.Value})
            .FirstOrDefaultAsync();

        // SQLite loses DateTime.Kind, and the host's global DateFormatString omits
        // offsets even for UTC DateTime values. Format after SQL materialization
        // as a string so the display formatter cannot remove the UTC marker.
        static string UtcIso(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            .ToString("O", CultureInfo.InvariantCulture);
        return new ThirdPartyContentTrackerPageBoundariesViewModel
        {
            Older = older == null ? null : new ThirdPartyContentTrackerPageBoundaryViewModel
                {ContentId = older.ContentId, UpdatedAt = UtcIso(older.UpdatedAt)},
            Newer = newer == null ? null : new ThirdPartyContentTrackerPageBoundaryViewModel
                {ContentId = newer.ContentId, UpdatedAt = UtcIso(newer.UpdatedAt)}
        };
    }

    public async Task<ThirdPartyContentTrackerDailyHistoryViewModel> GetDailyHistory(
        string domainKey,
        string? filter,
        string centerDay,
        string timeZoneId,
        IReadOnlyCollection<string> excludeContentIds)
    {
        if (!DateOnly.TryParseExact(centerDay, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var center))
            throw new ArgumentException("The center day must use yyyy-MM-dd.", nameof(centerDay));
        DateOnly startDay, endDay;
        try
        {
            startDay = center.AddDays(-15);
            endDay = center.AddDays(16);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentException("The center day must allow fifteen days on either side.", nameof(centerDay));
        }

        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            throw new ArgumentException("The requested time zone is not available.", nameof(timeZoneId));
        }

        DateTime UtcBoundary(DateOnly day)
        {
            var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            // A midnight skipped or repeated by a clock change has no unique UTC
            // boundary. Reject that window rather than silently losing an hour.
            if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
                throw new ArgumentException("The time zone has a skipped or repeated midnight at this window boundary.",
                    nameof(centerDay));
            var utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
            if (TimeZoneInfo.ConvertTimeFromUtc(utc, zone) != local)
                throw new ArgumentException("The date window exceeds the supported UTC range.", nameof(centerDay));
            return utc;
        }

        var startUtc = UtcBoundary(startDay);
        var endUtc = UtcBoundary(endDay);
        // A nullable Filter leaves a hole in SQLite's unique index. Resolve each
        // content ID's latest known content time before selecting this window;
        // an older duplicate must not bring content back from outside it.
        var contentTimes = await QueryHistoryRecords(domainKey, filter, excludeContentIds)
            .GroupBy(t => t.ContentId)
            .Select(group => group.Max(t => t.UpdatedAt)!.Value)
            .Where(time => time >= startUtc && time < endUtc).ToArrayAsync();
        var counts = new int[31];
        foreach (var time in contentTimes)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(time, DateTimeKind.Utc), zone);
            var index = DateOnly.FromDateTime(local).DayNumber - startDay.DayNumber;
            if (index is >= 0 and < 31) counts[index]++;
        }
        return new ThirdPartyContentTrackerDailyHistoryViewModel
        {
            Days = Enumerable.Range(0, 31).Select(index => new ThirdPartyContentTrackerDailyHistoryDayViewModel
            {
                Date = startDay.AddDays(index).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Count = counts[index]
            }).ToList()
        };
    }

    private IQueryable<ThirdPartyContentTrackerDbModel> QueryHistoryRecords(string domainKey, string? filter,
        IReadOnlyCollection<string> excludeContentIds)
    {
        ArgumentNullException.ThrowIfNull(excludeContentIds);
        var records = dbContext.ThirdPartyContentTrackers.AsNoTracking()
            .Where(t => t.DomainKey == domainKey && t.Filter == filter && t.UpdatedAt.HasValue);
        if (excludeContentIds.Count > 0)
        {
            var excluded = excludeContentIds.Distinct(StringComparer.Ordinal).ToArray();
            records = records.Where(t => !Enumerable.Contains(excluded, t.ContentId));
        }
        return records;
    }
}
