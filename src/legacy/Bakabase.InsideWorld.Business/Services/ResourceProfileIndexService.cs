using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Search;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>
/// Which resources match which profile, kept in memory both ways round.
/// <para>
/// The storage is a <see cref="SearchSetIndex"/>, shared with rule collections: a profile is a
/// search with configuration hanging off it and a rule collection is a search with a name on it,
/// and both are asked the same two questions. What is specific to profiles is here — the priority
/// ordering, the debounced task, and dropping the playable-file cache when a resource's profiles
/// change.
/// </para>
/// </summary>
public class ResourceProfileIndexService : IResourceProfileIndexService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly BTaskManager _taskManager;
    private readonly IBakabaseLocalizer _localizer;
    private readonly ILogger<ResourceProfileIndexService> _logger;

    private readonly SearchSetIndex _index = new();
    private readonly SemaphoreSlim _rebuildLock = new(1, 1);

    // Pending invalidation queues
    private readonly ConcurrentQueue<int> _pendingResourceInvalidations = new();
    private readonly ConcurrentQueue<int> _pendingProfileInvalidations = new();
    private volatile bool _pendingFullRebuild;

    // Debounce timer
    private Timer? _debounceTimer;
    private readonly object _debounceTimerLock = new();
    private const int DebounceDelayMs = 500;

    private const string TaskId = "ResourceProfileIndex";

    public ResourceProfileIndexService(
        IServiceProvider serviceProvider,
        BTaskManager taskManager,
        IBakabaseLocalizer localizer,
        IResourceDataChangeEvent resourceDataChangeEvent,
        ILogger<ResourceProfileIndexService> logger)
    {
        _serviceProvider = serviceProvider;
        _taskManager = taskManager;
        _localizer = localizer;
        _logger = logger;

        resourceDataChangeEvent.OnResourceDataChanged += args => InvalidateResources(args.ResourceIds);
        resourceDataChangeEvent.OnResourceRemoved += args => InvalidateResources(args.ResourceIds);
    }

    public bool IsReady => _index.IsReady;

    public Task WaitUntilReady(CancellationToken ct = default) => _index.WaitUntilReady(ct);

    public async Task<IReadOnlyList<int>> GetMatchingProfileIds(int resourceId)
    {
        await WaitUntilReady();

        return _index.GetSetIds(resourceId);
    }

    public async Task<Dictionary<int, IReadOnlyList<int>>> GetMatchingProfileIdsForResources(
        IEnumerable<int> resourceIds)
    {
        await WaitUntilReady();

        return resourceIds.Distinct().ToDictionary(id => id, id => _index.GetSetIds(id));
    }

    public async Task<IReadOnlySet<int>> GetMatchingResourceIds(int profileId)
    {
        await WaitUntilReady();

        return _index.GetMembers(profileId);
    }

    public void InvalidateResource(int resourceId)
    {
        _pendingResourceInvalidations.Enqueue(resourceId);
        ScheduleUpdate();
    }

    public void InvalidateResources(IEnumerable<int> resourceIds)
    {
        foreach (var id in resourceIds)
        {
            _pendingResourceInvalidations.Enqueue(id);
        }

        ScheduleUpdate();
    }

    public void InvalidateProfile(int profileId)
    {
        _pendingProfileInvalidations.Enqueue(profileId);
        ScheduleUpdate();
    }

    public void InvalidateAllProfiles()
    {
        _pendingFullRebuild = true;
        ScheduleUpdate();
    }

    public void TriggerFullRebuild()
    {
        _pendingFullRebuild = true;
        ScheduleUpdate();
    }

    public async Task RebuildAsync(Func<int, string?, Task>? onProgress, CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();

        await FullRebuild(scope.ServiceProvider.GetRequiredService<IResourceProfileService>(), onProgress, ct);
    }

    private void ScheduleUpdate()
    {
        lock (_debounceTimerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => EnqueueUpdateTask(), null, DebounceDelayMs, Timeout.Infinite);
        }
    }

    private void EnqueueUpdateTask()
    {
        lock (_debounceTimerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        // Already queued or running: it will pick up whatever is pending when it gets there.
        if (_taskManager.IsPending(TaskId)) return;

        var builder = BTaskBuilder.Create(TaskId)
            .Named(() => _localizer.BTask_Name("ResourceProfileIndex"))
            .Describe(() => _localizer.BTask_Description("ResourceProfileIndex"))
            .ConflictsWith(TaskId)
            .StartImmediately()
            .ReplaceIfExists()
            .Run(ProcessPendingUpdates);

        _ = _taskManager.Enqueue(builder);
    }

    private async Task ProcessPendingUpdates(BTaskArgs args)
    {
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var profiles = scope.ServiceProvider.GetRequiredService<IResourceProfileService>();

            if (_pendingFullRebuild || !_index.IsReady)
            {
                _pendingFullRebuild = false;

                // A full rebuild answers every pending question, so nothing is left queued to be
                // answered a second time.
                while (_pendingResourceInvalidations.TryDequeue(out _)) { }
                while (_pendingProfileInvalidations.TryDequeue(out _)) { }

                await FullRebuild(profiles, async (percentage, process) => await args.UpdateTask(t =>
                {
                    t.Percentage = percentage;
                    t.Process = process;
                }), args.CancellationToken);

                return;
            }

            var profilesToInvalidate = Drain(_pendingProfileInvalidations);
            var resourcesInvalidated = Drain(_pendingResourceInvalidations).Count > 0;

            // A resource changed: any profile's answer could have moved, and there is no way to ask
            // "does this one resource match" — a search is a search. So the profiles are all
            // re-evaluated, which is one search each rather than one per changed resource.
            var affected = resourcesInvalidated
                ? await ReindexAll(profiles, null, args.CancellationToken)
                : await Reindex(profilesToInvalidate, profiles, args.CancellationToken);

            await DropPlayableFileCache(scope.ServiceProvider, affected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ResourceProfile index updates");
        }
    }

    private async Task FullRebuild(IResourceProfileService profiles, Func<int, string?, Task>? onProgress,
        CancellationToken ct)
    {
        _logger.LogInformation("Starting full ResourceProfile index rebuild");

        await _rebuildLock.WaitAsync(ct);
        try
        {
            _index.Reset();

            await ReindexAll(profiles, onProgress, ct);

            _index.MarkReady();

            _logger.LogInformation("ResourceProfile index rebuild completed: {ProfileCount} profiles",
                _index.SetIds.Count);
        }
        finally
        {
            _rebuildLock.Release();
        }
    }

    /// <summary>Re-evaluates every profile. One search per profile, whatever prompted it.</summary>
    private async Task<HashSet<int>> ReindexAll(IResourceProfileService profiles,
        Func<int, string?, Task>? onProgress, CancellationToken ct)
    {
        var all = await profiles.GetAll();
        var affected = new HashSet<int>();
        var done = 0;

        // Ranks first: a profile indexed before its own rank is known would sort by zero.
        foreach (var profile in all) _index.SetRank(profile.Id, profile.Priority);

        // A profile that has gone leaves the index with everything it held.
        var known = all.Select(p => p.Id).ToHashSet();

        foreach (var setId in _index.SetIds)
        {
            if (!known.Contains(setId)) affected.UnionWith(_index.Remove(setId));
        }

        foreach (var profile in all)
        {
            ct.ThrowIfCancellationRequested();

            affected.UnionWith(_index.Replace(profile.Id, await profiles.GetMatchingResourceIds(profile.Search)));

            done++;

            if (onProgress != null) await onProgress(done * 100 / all.Count, $"{done}/{all.Count}");
        }

        return affected;
    }

    /// <summary>Re-evaluates only these profiles — one of them was added, edited or deleted.</summary>
    private async Task<HashSet<int>> Reindex(IReadOnlyCollection<int> profileIds,
        IResourceProfileService profiles, CancellationToken ct)
    {
        if (profileIds.Count == 0) return [];

        var byId = (await profiles.GetAll()).ToDictionary(p => p.Id);
        var affected = new HashSet<int>();

        foreach (var profileId in profileIds)
        {
            ct.ThrowIfCancellationRequested();

            if (!byId.TryGetValue(profileId, out var profile))
            {
                affected.UnionWith(_index.Remove(profileId));

                continue;
            }

            _index.SetRank(profileId, profile.Priority);

            // profile.Search directly rather than by id: asking the service by id would come back
            // through this index and answer with what it already believes.
            affected.UnionWith(_index.Replace(profileId, await profiles.GetMatchingResourceIds(profile.Search)));
        }

        return affected;
    }

    /// <summary>
    /// A resource's effective playable-file options come from its highest-priority matching profile,
    /// so a change in which profiles match makes any cached playable-file result stale. This is also
    /// what lets a freshly synced resource recover: it may have been discovered before it had been
    /// indexed against anything, and that empty result gets cached as a valid answer.
    /// </summary>
    private static async Task DropPlayableFileCache(IServiceProvider scope, IReadOnlyCollection<int> resourceIds)
    {
        if (resourceIds.Count == 0) return;

        await scope.GetRequiredService<IResourceService>()
            .DeleteResourceCacheByResourceIdsAndCacheType(resourceIds, ResourceCacheType.PlayableFiles);
    }

    private static List<int> Drain(ConcurrentQueue<int> queue)
    {
        var drained = new HashSet<int>();

        while (queue.TryDequeue(out var id)) drained.Add(id);

        return drained.ToList();
    }
}
