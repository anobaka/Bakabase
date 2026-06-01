using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.Subscription.Abstractions.Components;
using Bakabase.Modules.Subscription.Abstractions.Models.Db;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Subscription.Abstractions.Models.Input;
using Bakabase.Modules.Subscription.Abstractions.Services;
using Bakabase.Modules.Subscription.Extensions;
using Bakabase.Modules.Subscription.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Subscription.Services;

public class SubscriptionService<TDbContext> : ISubscriptionService
    where TDbContext : DbContext
{
    private readonly TDbContext _db;
    private readonly ISubscriptionProviderRegistry _providers;
    private readonly INotificationService _notifications;
    private readonly IWorkflowEventBus _workflowBus;
    private readonly ILogger<SubscriptionService<TDbContext>> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public SubscriptionService(
        TDbContext db,
        ISubscriptionProviderRegistry providers,
        INotificationService notifications,
        IWorkflowEventBus workflowBus,
        ILogger<SubscriptionService<TDbContext>> logger)
    {
        _db = db;
        _providers = providers;
        _notifications = notifications;
        _workflowBus = workflowBus;
        _logger = logger;
    }

    private DbSet<SubscriptionDbModel> Set => _db.Set<SubscriptionDbModel>();
    private DbSet<SubscriptionSnapshotDbModel> Snapshots => _db.Set<SubscriptionSnapshotDbModel>();

    public async Task<SubscriptionRecord> CreateAsync(SubscriptionCreationInputModel input, CancellationToken ct = default)
    {
        var provider = _providers.Get(input.Kind)
            ?? throw new InvalidOperationException($"Unknown subscription kind: {input.Kind}");

        var validation = await provider.ValidateTargetAsync(input.TargetJson, ct);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Error ?? "Invalid target");
        }

        var entity = new SubscriptionDbModel
        {
            Kind = input.Kind,
            DisplayName = input.DisplayName,
            TargetJson = input.TargetJson,
            Enabled = input.Enabled,
            IntervalMinutes = input.IntervalMinutes,
            CreatedAt = DateTime.Now,
        };

        Set.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.ToDomainModel();
    }

    public async Task<SubscriptionRecord> UpdateAsync(int id, SubscriptionUpdateInputModel input, CancellationToken ct = default)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"Subscription {id} not found");

        if (input.TargetJson is not null && input.TargetJson != entity.TargetJson)
        {
            var provider = _providers.Get(entity.Kind)
                ?? throw new InvalidOperationException($"Unknown subscription kind: {entity.Kind}");
            var validation = await provider.ValidateTargetAsync(input.TargetJson, ct);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.Error ?? "Invalid target");
            }
            entity.TargetJson = input.TargetJson;
        }

        if (input.DisplayName is not null) entity.DisplayName = input.DisplayName;
        if (input.Enabled is { } enabled) entity.Enabled = enabled;
        if (input.IntervalMinutes is { } interval) entity.IntervalMinutes = interval;

        await _db.SaveChangesAsync(ct);
        return entity.ToDomainModel();
    }

    public async Task DeleteAsync(int id)
    {
        await Set.Where(x => x.Id == id).ExecuteDeleteAsync();
        await Snapshots.Where(s => s.SubscriptionId == id).ExecuteDeleteAsync();
    }

    public async Task<SubscriptionRecord?> GetAsync(int id)
    {
        var entity = await Set.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return entity?.ToDomainModel();
    }

    public async Task<List<SubscriptionRecord>> SearchAsync(SubscriptionSearchInputModel input)
    {
        var query = Set.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(input.Kind)) query = query.Where(x => x.Kind == input.Kind);
        if (input.EnabledOnly == true) query = query.Where(x => x.Enabled);

        var rows = await query.OrderByDescending(x => x.Id).ToListAsync();
        return rows.Select(r => r.ToDomainModel()).ToList();
    }

    public async Task<SubscriptionCheckSummary?> RunCheckAsync(int id, CancellationToken ct = default)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return null;

        if (!_providers.TryGet(entity.Kind, out var provider))
        {
            var msg = $"Unknown subscription kind: {entity.Kind}";
            entity.LastError = msg;
            await _db.SaveChangesAsync(ct);
            return new SubscriptionCheckSummary { Error = msg };
        }

        var snapshotRow = await Snapshots.FirstOrDefaultAsync(s => s.SubscriptionId == id, ct);
        var firstRun = snapshotRow is null;
        var lastSnapshot = snapshotRow is null
            ? null
            : JsonSerializer.Deserialize<SubscriptionSnapshot>(snapshotRow.SnapshotJson, JsonOptions);

        SubscriptionCheckResult result;
        try
        {
            result = await provider.CheckAsync(entity.ToDomainModel(), lastSnapshot, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscription check failed: {Id} ({Kind})", entity.Id, entity.Kind);
            entity.LastError = ex.Message;
            entity.LastCheckedAt = DateTime.Now;
            await _db.SaveChangesAsync(CancellationToken.None);
            return new SubscriptionCheckSummary { FirstRun = firstRun, Error = ex.Message };
        }

        // Upsert the snapshot.
        var snapshotJson = JsonSerializer.Serialize(result.NewSnapshot, JsonOptions);
        if (snapshotRow is null)
        {
            Snapshots.Add(new SubscriptionSnapshotDbModel
            {
                SubscriptionId = id,
                SnapshotJson = snapshotJson,
                UpdatedAt = DateTime.Now,
            });
        }
        else
        {
            snapshotRow.SnapshotJson = snapshotJson;
            snapshotRow.UpdatedAt = DateTime.Now;
        }

        var changeCount = result.NewItems.Count + result.UpdatedItems.Count;
        entity.LastCheckedAt = DateTime.Now;
        entity.LastError = null;
        if (changeCount > 0) entity.LastChangeAt = DateTime.Now;

        await _db.SaveChangesAsync(ct);

        // Suppress notifications + workflow trigger on the very first run — the initial fetch
        // would mark every existing item as "new" and would be noisy / would flood any
        // configured workflows.
        if (changeCount > 0 && !firstRun)
        {
            await _notifications.CreateAsync(new NotificationCreationInputModel
            {
                Source = $"subscription:{entity.Id}",
                Title = entity.DisplayName,
                Body = BuildNotificationBody(result),
                Severity = AppNotificationSeverity.Info,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    subscriptionId = entity.Id,
                    kind = entity.Kind,
                    newItems = result.NewItems,
                    updatedItems = result.UpdatedItems,
                }, JsonOptions),
            });

            // Notification fires by default for every subscription; the workflow event is
            // additive — only matching enabled workflow definitions react.
            await _workflowBus.PublishAsync(
                SubscriptionWorkflowKinds.TriggerUpdated,
                new SubscriptionUpdatedPayload
                {
                    SubscriptionId = entity.Id,
                    Kind = entity.Kind,
                    DisplayName = entity.DisplayName,
                    NewItems = result.NewItems,
                    UpdatedItems = result.UpdatedItems,
                },
                ct);
        }

        return new SubscriptionCheckSummary
        {
            FirstRun = firstRun,
            NewItemCount = result.NewItems.Count,
            UpdatedItemCount = result.UpdatedItems.Count,
        };
    }

    private static string BuildNotificationBody(SubscriptionCheckResult result)
    {
        var newCount = result.NewItems.Count;
        var updatedCount = result.UpdatedItems.Count;
        var parts = new List<string>();
        if (newCount > 0) parts.Add($"{newCount} new");
        if (updatedCount > 0) parts.Add($"{updatedCount} updated");
        var headline = string.Join(", ", parts);

        var sample = result.NewItems.Concat(result.UpdatedItems).Take(3)
            .Select(i => i.Title ?? i.Id)
            .ToList();

        return sample.Count == 0 ? headline : $"{headline}\n{string.Join("\n", sample)}";
    }
}
