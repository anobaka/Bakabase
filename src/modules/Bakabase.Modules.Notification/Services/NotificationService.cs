using Bakabase.Modules.Notification.Abstractions.Components;
using Bakabase.Modules.Notification.Abstractions.Models.Db;
using Bakabase.Modules.Notification.Abstractions.Models.Domain;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Models.View;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.Notification.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Notification.Services;

public class NotificationService<TDbContext> : INotificationService
    where TDbContext : DbContext
{
    private readonly TDbContext _db;
    private readonly INotificationPusher _pusher;

    public NotificationService(TDbContext db, INotificationPusher pusher)
    {
        _db = db;
        _pusher = pusher;
    }

    private DbSet<NotificationDbModel> Set => _db.Set<NotificationDbModel>();

    public async Task<NotificationRecord> CreateAsync(NotificationCreationInputModel input)
    {
        var entity = new NotificationDbModel
        {
            Source = input.Source,
            Title = input.Title,
            Body = input.Body,
            PayloadJson = input.PayloadJson,
            Severity = input.Severity,
            CreatedAt = DateTime.Now,
            ReadAt = null,
        };

        Set.Add(entity);
        await _db.SaveChangesAsync();

        var domain = entity.ToDomainModel();
        await _pusher.PushPersistedAsync(NotificationViewModel.From(domain));

        return domain;
    }

    public async Task<SearchResponse<NotificationRecord>> SearchAsync(NotificationSearchInputModel input)
    {
        var query = Set.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(input.Source))
        {
            query = query.Where(x => x.Source == input.Source);
        }

        if (input.UnreadOnly)
        {
            query = query.Where(x => x.ReadAt == null);
        }

        var pageIndex = Math.Max(1, input.PageIndex);
        var pageSize = Math.Clamp(input.PageSize, 1, 200);

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new SearchResponse<NotificationRecord>(
            rows.Select(r => r.ToDomainModel()),
            total,
            pageIndex,
            pageSize);
    }

    public Task<int> GetUnreadCountAsync() =>
        Set.AsNoTracking().CountAsync(x => x.ReadAt == null);

    public async Task MarkAsReadAsync(int[]? ids)
    {
        var now = DateTime.Now;
        var query = Set.Where(x => x.ReadAt == null);

        if (ids is { Length: > 0 })
        {
            var set = ids.ToHashSet();
            query = query.Where(x => set.Contains(x.Id));
        }

        await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, _ => now));
    }

    public async Task DeleteAsync(int[] ids)
    {
        if (ids.Length == 0) return;
        var set = ids.ToHashSet();
        await Set.Where(x => set.Contains(x.Id)).ExecuteDeleteAsync();
    }

    public Task ClearReadAsync() =>
        Set.Where(x => x.ReadAt != null).ExecuteDeleteAsync();
}
