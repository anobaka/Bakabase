using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Collection.Abstractions.Models.Domain;
using Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.Subscription.Abstractions.Components;
using Bakabase.Modules.Subscription.Abstractions.Models.Db;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Subscription.Abstractions.Models.Input;
using Bakabase.Modules.Subscription.Abstractions.Services;
using Bakabase.Modules.Subscription.Extensions;
using Bakabase.Modules.Subscription.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Subscription.Services;

/// <summary>
/// Subscriptions, which are collections' external sources.
/// <para>
/// A check turns what a source currently lists into members of a collection. Everything a source
/// finds becomes a real resource — the thing you do not have yet — rather than a row in a snapshot
/// table, which is what makes a subscription worth having: it fills a list you can then act on.
/// </para>
/// </summary>
public class SubscriptionService<TDbContext> : ISubscriptionService
    where TDbContext : DbContext
{
    private readonly TDbContext _db;
    private readonly ISubscriptionProviderRegistry _providers;
    private readonly INotificationService _notifications;
    private readonly IWorkflowEventBus _workflowBus;
    private readonly ICollectionService _collections;
    private readonly ICollectionResourceMappingService _mappings;
    private readonly IPlaceholderResourceService _placeholders;
    private readonly IAcquisitionLeadService _leads;
    private readonly IResourceService _resources;
    private readonly IServiceProvider _serviceProvider;
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
        ICollectionService collections,
        ICollectionResourceMappingService mappings,
        IPlaceholderResourceService placeholders,
        IAcquisitionLeadService leads,
        IResourceService resources,
        IServiceProvider serviceProvider,
        ILogger<SubscriptionService<TDbContext>> logger)
    {
        _db = db;
        _providers = providers;
        _notifications = notifications;
        _workflowBus = workflowBus;
        _collections = collections;
        _mappings = mappings;
        _placeholders = placeholders;
        _leads = leads;
        _resources = resources;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private DbSet<SubscriptionDbModel> Set => _db.Set<SubscriptionDbModel>();

    public async Task<SubscriptionRecord> CreateAsync(SubscriptionCreationInputModel input,
        CancellationToken ct = default)
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
            CollectionId = input.CollectionId ?? await CreateCollectionFor(input.DisplayName, ct),
            CreatedAt = DateTime.Now,
        };

        Set.Add(entity);
        await _db.SaveChangesAsync(ct);

        return entity.ToDomainModel();
    }

    public async Task<SubscriptionRecord> UpdateAsync(int id, SubscriptionUpdateInputModel input,
        CancellationToken ct = default)
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
        if (input.CollectionId is { } collectionId) entity.CollectionId = collectionId;

        await _db.SaveChangesAsync(ct);

        return entity.ToDomainModel();
    }

    /// <summary>
    /// Deleting a source leaves its collection and everything in it. What a subscription found is
    /// yours whether or not you are still watching where it came from.
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        await Set.Where(x => x.Id == id).ExecuteDeleteAsync();
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

            return new SubscriptionCheckSummary {Error = msg};
        }

        var collectionId = entity.CollectionId ?? await CreateCollectionFor(entity.DisplayName, ct);

        entity.CollectionId = collectionId;

        // What was already here before this check. A first check has nothing, which is exactly
        // what makes it a first check.
        var known = (await _mappings.GetByCollectionId(collectionId))
            .Where(m => m.SubscriptionId == entity.Id)
            .ToList();
        var firstRun = known.Count == 0;

        IReadOnlyList<SubscriptionItem> items;
        try
        {
            items = await provider.FetchAllItemsAsync(entity.ToDomainModel(), ct);
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

            return new SubscriptionCheckSummary {FirstRun = firstRun, Error = ex.Message};
        }

        var knownResourceIds = known.Select(m => m.ResourceId).ToHashSet();
        var seen = new HashSet<int>();
        var added = new List<int>();

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            int resourceId;
            try
            {
                resourceId = await ResolveResource(provider, item, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad item must not cost the whole check. A listing of two hundred works with
                // one unreadable entry should still bring in the other hundred and ninety-nine.
                _logger.LogWarning(ex, "[Subscription] Could not make a resource of {SourceKey} from {Kind}",
                    item.SourceKey, entity.Kind);
                continue;
            }

            seen.Add(resourceId);

            if (!knownResourceIds.Contains(resourceId)) added.Add(resourceId);

            await _collections.AddMembers(collectionId, [resourceId],
                CollectionMembershipOrigin.Subscription, entity.Id, ct);
        }

        // Still listed: seen again just now. The mapping's LastSeenAt is what tells a member that
        // has quietly disappeared from a circle's page apart from one that never left.
        await _mappings.MarkSeen(collectionId, seen, DateTime.Now, ct);

        entity.LastCheckedAt = DateTime.Now;
        entity.LastError = null;
        if (added.Count > 0) entity.LastChangeAt = DateTime.Now;

        await _db.SaveChangesAsync(ct);

        // The first check is the collection's seed, so it is recorded like any other — but it is
        // not news. Announcing two hundred works as "new" would be true and useless.
        if (added.Count > 0 && !firstRun)
        {
            await Announce(entity, collectionId, added, ct);
        }

        if (added.Count > 0) await AutoAcquireIfWanted(collectionId, added, provider, items, ct);

        return new SubscriptionCheckSummary
        {
            FirstRun = firstRun,
            NewItemCount = added.Count,
            UpdatedItemCount = 0,
        };
    }

    // ------- the two routes an item can take -------

    /// <summary>
    /// The resource this item is about, created if nothing stands for it yet.
    /// <para>
    /// Which route it takes is the source's kind and nothing else: a platform or a catalog names
    /// works, so its key is an identity; a sharing channel names posts, so its key is only a way of
    /// telling one post from another and the link becomes a lead.
    /// </para>
    /// </summary>
    private async Task<int> ResolveResource(ISubscriptionProvider provider, SubscriptionItem item,
        CancellationToken ct)
    {
        var known = new KnownItemDetail(item.Title, item.CoverUrls, item.MetadataJson);

        if (provider.SourceKind == SubscriptionSourceKind.SharingChannel)
        {
            var url = item.Url;

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException(
                    $"A shared item needs a link; {item.SourceKey} had none.");
            }

            return (await _placeholders.CreateOrMatchBySharedUrl(url, known, ct)).ResourceId;
        }

        if (provider.ResourceSource is not { } source)
        {
            throw new InvalidOperationException(
                $"{provider.Kind} lists works but names no identity namespace.");
        }

        return (await _placeholders.CreateOrMatchByExternalIdentity(source, item.SourceKey, known, ct))
            .ResourceId;
    }

    private async Task<int> CreateCollectionFor(string displayName, CancellationToken ct) =>
        (await _collections.Add(new CollectionInputModel {Name = displayName}, ct)).Id;

    private async Task Announce(SubscriptionDbModel entity, int collectionId, List<int> added,
        CancellationToken ct)
    {
        var progress = await _collections.GetProgress(collectionId, ct);
        // Read once here rather than by each activity that wants a name.
        var resources = await _resources.GetByKeys(added.ToArray());

        await _notifications.CreateAsync(new NotificationCreationInputModel
        {
            // Addressed to the collection rather than the subscription: the collection is what the
            // user opened, and two sources filling one collection should read as one thing.
            Source = $"collection:{collectionId}",
            Title = entity.DisplayName,
            Body = $"{added.Count} new · {(int) Math.Round(progress.Ratio * 100)}% collected",
            Severity = AppNotificationSeverity.Info,
            PayloadJson = JsonSerializer.Serialize(new
            {
                collectionId,
                subscriptionId = entity.Id,
                route = $"/collections/detail?id={collectionId}",
                resourceIds = added,
            }, JsonOptions),
        });

        await _workflowBus.PublishAsync(
            SubscriptionWorkflowKinds.TriggerUpdated,
            new SubscriptionUpdatedPayload
            {
                SubscriptionId = entity.Id,
                Kind = entity.Kind,
                DisplayName = entity.DisplayName,
                CollectionId = collectionId,
                Resources = resources
                    .Select(r => new SubscriptionResourceSummary(r.Id, r.DisplayName ?? r.FileName, r.Path))
                    .ToList(),
            },
            ct);
    }

    /// <summary>
    /// Start getting the new members, when the collection says to.
    /// <para>
    /// A sharing channel's item carries its own link, so it can be fetched at once. A platform
    /// holding is fetched through its identity. A catalog entry has neither, and waits for someone
    /// to say where to get it — which is why the collection page shows it as missing rather than
    /// pretending to try.
    /// </para>
    /// </summary>
    private async Task AutoAcquireIfWanted(int collectionId, List<int> added,
        ISubscriptionProvider provider, IReadOnlyList<SubscriptionItem> items, CancellationToken ct)
    {
        var collection = await _collections.Get(collectionId, false, ct);

        if (collection?.AutoAcquire != true) return;

        // Resolved late and optionally: a build without the acquisition pipeline still collects.
        if (_serviceProvider.GetService(typeof(IAcquisitionService)) is not IAcquisitionService acquisitions)
        {
            return;
        }

        // A collection is usually one kind of thing, so how to get one is how to get all of them.
        var settings = CollectionAcquisitionSettings.Read(collection.AcquisitionSettingsJson);

        foreach (var resourceId in added)
        {
            ct.ThrowIfCancellationRequested();

            var lead = (await _leads.GetByResourceId(resourceId)).FirstOrDefault();

            if (lead == null) continue;

            try
            {
                await acquisitions.CreateAsync(resourceId, lead.Kind, lead.Value,
                    lead.Id == 0 ? null : lead.Id, settings?.RecipeDefinitionId, collectionId, ct);
            }
            catch (InvalidOperationException ex)
            {
                // Already being acquired, already here, or no recipe for that kind of lead. None
                // of those should stop the rest of the batch.
                _logger.LogInformation("[Subscription] Not acquiring resource {ResourceId}: {Reason}",
                    resourceId, ex.Message);
            }
        }
    }
}
