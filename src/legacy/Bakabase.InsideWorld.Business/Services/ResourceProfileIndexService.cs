using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>
/// Maintains an in-memory index for Resource-ResourceProfile matching.
/// </summary>
public class ResourceProfileIndexService : IResourceProfileIndexService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly BTaskManager _taskManager;
    private readonly IBakabaseLocalizer _localizer;
    private readonly ILogger<ResourceProfileIndexService> _logger;

    // resourceId => profileIds (sorted by priority, highest first)
    private readonly ConcurrentDictionary<int, IReadOnlyList<int>> _resourceToProfiles = new();

    // profileId => resourceIds
    private readonly ConcurrentDictionary<int, IReadOnlySet<int>> _profileToResources = new();

    // Cache of profile priorities for sorting
    private readonly ConcurrentDictionary<int, int> _profilePriorities = new();

    private volatile bool _isReady;
    private readonly SemaphoreSlim _readyLock = new(1, 1);
    private TaskCompletionSource _readyTcs = new();

    // Pending invalidation queues
    private readonly ConcurrentQueue<int> _pendingResourceInvalidations = new();
    private readonly ConcurrentQueue<int> _pendingProfileInvalidations = new();
    // Int32 so consuming a rebuild request cannot overwrite a concurrent request.
    private int _pendingFullRebuild;

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

        // Subscribe to resource data change events
        resourceDataChangeEvent.OnResourceDataChanged += OnResourceDataChanged;
        resourceDataChangeEvent.OnResourceRemoved += OnResourceRemoved;
    }

    private void OnResourceDataChanged(ResourceDataChangedEventArgs args)
    {
        InvalidateResources(args.ResourceIds);
    }

    private void OnResourceRemoved(ResourceRemovedEventArgs args)
    {
        // When resources are removed, we need to invalidate them to remove from index
        InvalidateResources(args.ResourceIds);
    }

    public bool IsReady => _isReady;

    public async Task WaitUntilReady(CancellationToken ct = default)
    {
        if (_isReady) return;

        // Use a copy of the TCS to avoid race conditions
        var tcs = _readyTcs;
        await using (ct.Register(() => tcs.TrySetCanceled()))
        {
            await tcs.Task;
        }
    }

    public async Task<IReadOnlyList<int>> GetMatchingProfileIds(int resourceId)
    {
        await WaitUntilReady();
        return _resourceToProfiles.TryGetValue(resourceId, out var profileIds) ? profileIds : Array.Empty<int>();
    }

    public async Task<Dictionary<int, IReadOnlyList<int>>> GetMatchingProfileIdsForResources(IEnumerable<int> resourceIds)
    {
        await WaitUntilReady();
        var result = new Dictionary<int, IReadOnlyList<int>>();
        foreach (var resourceId in resourceIds)
        {
            if (_resourceToProfiles.TryGetValue(resourceId, out var profileIds))
            {
                result[resourceId] = profileIds;
            }
            else
            {
                result[resourceId] = Array.Empty<int>();
            }
        }
        return result;
    }

    public async Task<IReadOnlySet<int>> GetMatchingResourceIds(int profileId)
    {
        await WaitUntilReady();
        return _profileToResources.TryGetValue(profileId, out var resourceIds)
            ? resourceIds
            : new HashSet<int>();
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
        Volatile.Write(ref _pendingFullRebuild, 1);
        ScheduleUpdate();
    }

    public void TriggerFullRebuild()
    {
        Volatile.Write(ref _pendingFullRebuild, 1);
        ScheduleUpdate();
    }

    public async Task RebuildAsync(Func<int, string?, Task>? onProgress, CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var resourceProfileService = sp.GetRequiredService<IResourceProfileService>();
        var resourceService = sp.GetRequiredService<IResourceService>();

        await FullRebuildCore(resourceProfileService, resourceService, onProgress, ct);
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

        // Check if there's already a pending task
        if (_taskManager.IsPending(TaskId))
        {
            // The active task may already have drained its snapshot. Keep checking until it
            // finishes so invalidations arriving during its run cannot remain queued forever.
            if (HasPendingUpdates())
            {
                ScheduleUpdate();
            }

            return;
        }

        if (!HasPendingUpdates()) return;

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
            var sp = scope.ServiceProvider;
            var resourceProfileService = sp.GetRequiredService<IResourceProfileService>();
            var resourceService = sp.GetRequiredService<IResourceService>();
            var resourceSearchIndexService = sp.GetRequiredService<IResourceSearchIndexService>();

            // Check if full rebuild is needed
            if (Volatile.Read(ref _pendingFullRebuild) != 0 || !_isReady)
            {
                // Consume the request before taking the queue snapshot. A rebuild requested
                // after this exchange remains pending for a follow-up task instead of being
                // overwritten when this rebuild finishes.
                Interlocked.Exchange(ref _pendingFullRebuild, 0);

                // This rebuild supersedes the invalidations already queued. Drain them before
                // placing the search-index barrier so changes arriving afterwards remain queued
                // for the next task rather than being discarded after the barrier snapshot.
                while (_pendingResourceInvalidations.TryDequeue(out _)) { }
                while (_pendingProfileInvalidations.TryDequeue(out _)) { }

                try
                {
                    await ReportIncrementalProgress(
                        args,
                        1,
                        "ResourceProfileIndex_WaitingForSearchIndex");
                    await WaitForSearchIndexOrFallback(resourceSearchIndexService, args.CancellationToken);
                    await FullRebuild(resourceProfileService, resourceService, args);
                }
                catch
                {
                    // A cancelled or failed rebuild did not establish a complete profile index.
                    // Keep the request available for an explicit restart (cancellation) or the
                    // automatically scheduled retry (other failures).
                    Volatile.Write(ref _pendingFullRebuild, 1);
                    throw;
                }

                return;
            }

            await ReportIncrementalProgress(
                args,
                1,
                "ResourceProfileIndex_LoadingIncrementalUpdates");

            // Process profile invalidations first (they may require re-evaluating all resources)
            var profilesToInvalidate = new HashSet<int>();
            while (_pendingProfileInvalidations.TryDequeue(out var profileId))
            {
                profilesToInvalidate.Add(profileId);
            }

            var resourcesToInvalidate = new HashSet<int>();
            while (_pendingResourceInvalidations.TryDequeue(out var resourceId))
            {
                resourcesToInvalidate.Add(resourceId);
            }

            if (profilesToInvalidate.Count == 0 && resourcesToInvalidate.Count == 0) return;

            await ReportIncrementalProgress(
                args,
                3,
                "ResourceProfileIndex_WaitingForSearchIndex");

            // Resource data changes enqueue both search-index and profile-index work. Matching
            // profiles before the search-index queue reaches this snapshot can cache stale
            // results, so place a FIFO barrier immediately before evaluating any profile.
            try
            {
                await WaitForSearchIndexOrFallback(resourceSearchIndexService, args.CancellationToken);
            }
            catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
            {
                RequeueInvalidations(profilesToInvalidate, resourcesToInvalidate, false);
                throw;
            }

            try
            {
                if (profilesToInvalidate.Count > 0)
                {
                    await ProcessProfileInvalidations(
                        profilesToInvalidate,
                        resourceProfileService,
                        resourceService,
                        args,
                        5,
                        resourcesToInvalidate.Count > 0 ? 45 : 95);
                }

                if (resourcesToInvalidate.Count > 0)
                {
                    await ProcessResourceInvalidations(
                        resourcesToInvalidate,
                        resourceProfileService,
                        resourceService,
                        args,
                        profilesToInvalidate.Count > 0 ? 50 : 5,
                        95);
                }
            }
            catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
            {
                RequeueInvalidations(profilesToInvalidate, resourcesToInvalidate, false);
                throw;
            }
            catch
            {
                // Fallible asynchronous reads and cache persistence finish before either
                // direction publishes its copy-on-write mapping, so this snapshot is retryable.
                RequeueInvalidations(profilesToInvalidate, resourcesToInvalidate, true);
                throw;
            }
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ResourceProfile index updates");
            if (HasPendingUpdates()) ScheduleUpdate();
            throw;
        }
    }

    private void RequeueInvalidations(
        IEnumerable<int> profileIds,
        IEnumerable<int> resourceIds,
        bool scheduleUpdate)
    {
        foreach (var profileId in profileIds)
        {
            _pendingProfileInvalidations.Enqueue(profileId);
        }

        foreach (var resourceId in resourceIds)
        {
            _pendingResourceInvalidations.Enqueue(resourceId);
        }

        if (scheduleUpdate) ScheduleUpdate();
    }

    private bool HasPendingUpdates() =>
        Volatile.Read(ref _pendingFullRebuild) != 0 ||
        !_pendingProfileInvalidations.IsEmpty ||
        !_pendingResourceInvalidations.IsEmpty;

    private async Task WaitForSearchIndexOrFallback(
        IResourceSearchIndexService resourceSearchIndexService,
        CancellationToken ct)
    {
        try
        {
            await resourceSearchIndexService.WaitForPendingUpdatesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed incremental search-index batch marks that index unavailable. Resource
            // searches then deliberately fall back to a full scan, which is still a correct
            // source for rebuilding profile mappings and avoids an endless retry loop here.
            _logger.LogWarning(ex,
                "Resource search index barrier failed; ResourceProfile matching will use the full-scan fallback");
        }
    }

    private async Task FullRebuild(
        IResourceProfileService resourceProfileService,
        IResourceService resourceService,
        BTaskArgs args)
    {
        await FullRebuildCore(
            resourceProfileService,
            resourceService,
            async (percentage, process) =>
            {
                await args.UpdateTask(t =>
                {
                    t.Percentage = percentage;
                    t.Process = process;
                });
            },
            args.CancellationToken);
    }

    private async Task FullRebuildCore(
        IResourceProfileService resourceProfileService,
        IResourceService resourceService,
        Func<int, string?, Task>? onProgress,
        CancellationToken ct)
    {
        _logger.LogInformation("Starting full ResourceProfile index rebuild");

        try
        {
            await _readyLock.WaitAsync(ct);

            _isReady = false;
            _resourceToProfiles.Clear();
            _profileToResources.Clear();
            _profilePriorities.Clear();

            // Get all profiles
            var profiles = await resourceProfileService.GetAll();
            var totalProfiles = profiles.Count;

            if (totalProfiles == 0)
            {
                _isReady = true;
                _readyTcs.TrySetResult();
                return;
            }

            // Store priorities
            foreach (var profile in profiles)
            {
                _profilePriorities[profile.Id] = profile.Priority;
            }

            // Get all resource IDs (pass empty search to get all)
            var allResourceIds = await resourceService.GetAllIds(new ResourceSearch { PageSize = int.MaxValue });

            // Build index per profile
            var processedProfiles = 0;
            foreach (var profile in profiles)
            {
                ct.ThrowIfCancellationRequested();

                var matchingResourceIds = await resourceProfileService.GetMatchingResourceIds(profile.Id);
                _profileToResources[profile.Id] = matchingResourceIds;

                // Update reverse index
                foreach (var resourceId in matchingResourceIds)
                {
                    _resourceToProfiles.AddOrUpdate(
                        resourceId,
                        _ => new List<int> { profile.Id },
                        (_, existing) =>
                        {
                            var list = existing.ToList();
                            if (!list.Contains(profile.Id))
                            {
                                list.Add(profile.Id);
                                // Sort by priority (highest first)
                                list.Sort((a, b) =>
                                    _profilePriorities.GetValueOrDefault(b, 0)
                                        .CompareTo(_profilePriorities.GetValueOrDefault(a, 0)));
                            }

                            return list;
                        });
                }

                processedProfiles++;
                var percentage = (int)(processedProfiles * 100f / totalProfiles);
                if (onProgress != null)
                {
                    await onProgress(percentage, $"{processedProfiles}/{totalProfiles}");
                }
            }

            _isReady = true;
            _readyTcs.TrySetResult();
            _logger.LogInformation(
                "ResourceProfile index rebuild completed: {ProfileCount} profiles, {ResourceCount} resources indexed",
                totalProfiles, _resourceToProfiles.Count);
        }
        finally
        {
            _readyLock.Release();
        }
    }

    private async Task ProcessProfileInvalidations(
        HashSet<int> profileIds,
        IResourceProfileService resourceProfileService,
        IResourceService resourceService,
        BTaskArgs args,
        int progressStart,
        int progressEnd)
    {
        _logger.LogDebug("Processing {Count} profile invalidations", profileIds.Count);

        await ReportIncrementalProgress(
            args,
            progressStart,
            "ResourceProfileIndex_ProcessingProfileInvalidations",
            0,
            profileIds.Count);

        // Get current profiles
        var allProfiles = await resourceProfileService.GetAll();
        var profileMap = allProfiles.ToDictionary(p => p.Id);

        // Resources touched by a profile add/edit/delete: their effective playable-file options
        // may have changed (the profile gained/lost them, or its playable rules were edited), so
        // any cached playable-file result must be invalidated to be re-discovered on next access.
        var affectedResourceIds = new HashSet<int>();
        var rebuiltResourceIdsByProfile = new Dictionary<int, HashSet<int>?>(profileIds.Count);
        var processedProfiles = 0;
        var processingProgressEnd = Math.Max(progressStart, progressEnd - 5);

        // Resolve every new matching set before mutating either direction of the cache. If one
        // search fails, the queued snapshot can be retried without having lost the old mapping
        // needed to invalidate playable-file caches correctly.
        foreach (var profileId in profileIds)
        {
            await args.YieldAsync();

            if (profileMap.TryGetValue(profileId, out var profile))
            {
                // Use profile.Search directly to bypass index cache and avoid circular dependency
                rebuiltResourceIdsByProfile[profileId] =
                    await resourceProfileService.GetMatchingResourceIds(profile.Search);
            }
            else
            {
                rebuiltResourceIdsByProfile[profileId] = null;
            }

            processedProfiles++;
            await ReportIncrementalProgress(
                args,
                ScaleProgress(progressStart, processingProgressEnd, processedProfiles, profileIds.Count),
                "ResourceProfileIndex_ProcessingProfileInvalidations",
                processedProfiles,
                profileIds.Count);
        }

        var oldResourceIdsByProfile = profileIds.ToDictionary(
            profileId => profileId,
            profileId => _profileToResources.GetValueOrDefault(profileId) ?? new HashSet<int>());
        foreach (var profileId in profileIds)
        {
            affectedResourceIds.UnionWith(oldResourceIdsByProfile[profileId]);
            if (rebuiltResourceIdsByProfile[profileId] is { } newResourceIds)
            {
                affectedResourceIds.UnionWith(newResourceIds);
            }
        }

        // Clear derived data before publishing the new mapping. If cache persistence fails,
        // no mapping has been changed yet and retrying the queued snapshot remains lossless.
        if (affectedResourceIds.Count > 0)
        {
            await ReportIncrementalProgress(
                args,
                Math.Max(processingProgressEnd, progressEnd - 2),
                "ResourceProfileIndex_InvalidatingPlayableFileCaches",
                affectedResourceIds.Count);
            await resourceService.DeleteResourceCacheByResourceIdsAndCacheType(
                affectedResourceIds, ResourceCacheType.PlayableFiles);
        }

        await args.YieldAsync();
        await ReportIncrementalProgress(
            args,
            progressEnd,
            "ResourceProfileIndex_UpdatingResourceMappings",
            affectedResourceIds.Count);

        foreach (var profileId in profileIds)
        {
            var oldResourceIds = oldResourceIdsByProfile[profileId];
            var newResourceIds = rebuiltResourceIdsByProfile[profileId];

            foreach (var resourceId in oldResourceIds)
            {
                RemoveProfileFromResource(resourceId, profileId);
            }

            if (newResourceIds != null)
            {
                _profilePriorities[profileId] = profileMap[profileId].Priority;
                _profileToResources[profileId] = newResourceIds;

                foreach (var resourceId in newResourceIds)
                {
                    AddProfileToResource(resourceId, profileId);
                }
            }
            else
            {
                _profileToResources.TryRemove(profileId, out _);
                _profilePriorities.TryRemove(profileId, out _);
            }
        }
    }

    private async Task ProcessResourceInvalidations(
        HashSet<int> resourceIds,
        IResourceProfileService resourceProfileService,
        IResourceService resourceService,
        BTaskArgs args,
        int progressStart,
        int progressEnd)
    {
        _logger.LogDebug("Processing {Count} resource invalidations", resourceIds.Count);

        await ReportIncrementalProgress(
            args,
            progressStart,
            "ResourceProfileIndex_LoadingProfilesForResources",
            resourceIds.Count);

        var allProfiles = await resourceProfileService.GetAll();

        foreach (var profile in allProfiles)
        {
            _profilePriorities[profile.Id] = profile.Priority;
        }

        var activeProfileIds = allProfiles.Select(p => p.Id).ToHashSet();
        var oldProfileIdsByResource = resourceIds.ToDictionary(
            resourceId => resourceId,
            resourceId => _resourceToProfiles.TryGetValue(resourceId, out var existing)
                ? existing
                : Array.Empty<int>());

        // Resources whose matching-profile set actually changed. Their effective playable-file
        // options come from the highest-priority matching profile, so a change here can make a
        // previously cached playable-file result stale. This is what lets a freshly-synced
        // resource auto-recover: it may have been discovered with an empty playable result
        // before it had been indexed against any profile (GetMatchingProfileIds returned none),
        // which gets cached as a valid "no playable files" entry; once indexing catches up we
        // invalidate that entry so the next access re-discovers from the (now resolvable) profile.
        var matchingChangedResourceIds = new HashSet<int>();

        // Evaluate each profile exactly once. The previous resource-major loop executed the same
        // full matching search once per invalidated resource, even though every result was the
        // complete set of resources matching that profile.
        var matchingProfileIdsByResource = resourceIds.ToDictionary(id => id, _ => new List<int>());
        var affectedMatchesByProfile = new Dictionary<int, HashSet<int>>(allProfiles.Count);
        var processedProfiles = 0;
        var matchingProgressEnd = Math.Max(progressStart, progressEnd - 10);

        foreach (var profile in allProfiles)
        {
            await args.YieldAsync();

            // Use profile.Search directly to bypass this index and avoid a circular lookup.
            var matchingResourceIds = await resourceProfileService.GetMatchingResourceIds(profile.Search);
            matchingResourceIds.IntersectWith(resourceIds);
            affectedMatchesByProfile[profile.Id] = matchingResourceIds;

            foreach (var resourceId in matchingResourceIds)
            {
                matchingProfileIdsByResource[resourceId].Add(profile.Id);
            }

            processedProfiles++;
            await ReportIncrementalProgress(
                args,
                ScaleProgress(progressStart, matchingProgressEnd, processedProfiles, allProfiles.Count),
                "ResourceProfileIndex_MatchingProfilesForResources",
                processedProfiles,
                allProfiles.Count,
                resourceIds.Count);
        }

        foreach (var resourceId in resourceIds)
        {
            var oldProfileIds = oldProfileIdsByResource[resourceId];
            var matchingProfileIds = matchingProfileIdsByResource[resourceId];
            if (oldProfileIds.Count != matchingProfileIds.Count ||
                !oldProfileIds.ToHashSet().SetEquals(matchingProfileIds))
            {
                matchingChangedResourceIds.Add(resourceId);
            }
        }

        // Persist cache invalidation while the old mappings are still intact. A failure leaves
        // this snapshot fully retryable; after success the remaining mapping update is in-memory
        // and contains no cancellation points that could expose a partially applied batch.
        if (matchingChangedResourceIds.Count > 0)
        {
            await ReportIncrementalProgress(
                args,
                Math.Max(progressStart, progressEnd - 5),
                "ResourceProfileIndex_InvalidatingPlayableFileCaches",
                matchingChangedResourceIds.Count);
            await resourceService.DeleteResourceCacheByResourceIdsAndCacheType(
                matchingChangedResourceIds, ResourceCacheType.PlayableFiles);
        }

        await args.YieldAsync();
        await ReportIncrementalProgress(
            args,
            progressEnd,
            "ResourceProfileIndex_UpdatingResourceMappings",
            resourceIds.Count);

        // Update the profile -> resources side in one copy-on-write operation per profile. Only
        // this invalidation batch is replaced; unrelated resources retain their prior membership
        // even if another change is queued while these searches are running.
        foreach (var profile in allProfiles)
        {
            var newMatches = affectedMatchesByProfile[profile.Id];
            _profileToResources.AddOrUpdate(
                profile.Id,
                _ => new HashSet<int>(newMatches),
                (_, existing) =>
                {
                    var updated = existing.ToHashSet();
                    updated.ExceptWith(resourceIds);
                    updated.UnionWith(newMatches);
                    return updated;
                });
        }

        // Preserve the old implementation's cleanup for cached profiles that are no longer
        // returned by the profile service. Their dedicated profile invalidation will remove the
        // rest of the stale mapping; this batch must still detach the resources in its snapshot.
        foreach (var staleProfileId in oldProfileIdsByResource.Values
                     .SelectMany(ids => ids)
                     .Where(id => !activeProfileIds.Contains(id))
                     .Distinct())
        {
            _profileToResources.AddOrUpdate(
                staleProfileId,
                _ => new HashSet<int>(),
                (_, existing) =>
                {
                    var updated = existing.ToHashSet();
                    updated.ExceptWith(resourceIds);
                    return updated;
                });

            if (_profileToResources.TryGetValue(staleProfileId, out var remaining) && remaining.Count == 0)
            {
                _profileToResources.TryRemove(staleProfileId, out _);
            }
        }

        foreach (var resourceId in resourceIds)
        {
            var matchingProfileIds = matchingProfileIdsByResource[resourceId];

            if (matchingProfileIds.Count > 0)
            {
                // Sort by priority
                matchingProfileIds.Sort((a, b) =>
                    _profilePriorities.GetValueOrDefault(b, 0)
                        .CompareTo(_profilePriorities.GetValueOrDefault(a, 0)));
                _resourceToProfiles[resourceId] = matchingProfileIds;
            }
            else
            {
                _resourceToProfiles.TryRemove(resourceId, out _);
            }
        }
    }

    private async Task ReportIncrementalProgress(
        BTaskArgs args,
        int percentage,
        string processKey,
        params object?[] processArguments)
    {
        await args.UpdateTask(t =>
        {
            t.Percentage = Math.Max(t.Percentage, percentage);
            t.SetProcess(BTaskText.Localize(_localizer, processKey, processArguments));
        });
    }

    private static int ScaleProgress(int start, int end, int completed, int total) =>
        total <= 0 ? end : start + (int)((long)(end - start) * completed / total);

    private void AddProfileToResource(int resourceId, int profileId)
    {
        _resourceToProfiles.AddOrUpdate(
            resourceId,
            _ => new List<int> { profileId },
            (_, existing) =>
            {
                var list = existing.ToList();
                if (!list.Contains(profileId))
                {
                    list.Add(profileId);
                    list.Sort((a, b) =>
                        _profilePriorities.GetValueOrDefault(b, 0)
                            .CompareTo(_profilePriorities.GetValueOrDefault(a, 0)));
                }

                return list;
            });
    }

    private void RemoveProfileFromResource(int resourceId, int profileId)
    {
        _resourceToProfiles.AddOrUpdate(
            resourceId,
            _ => Array.Empty<int>(),
            (_, existing) =>
            {
                var list = existing.ToList();
                list.Remove(profileId);
                return list;
            });

        // Clean up empty entries
        if (_resourceToProfiles.TryGetValue(resourceId, out var profiles) && profiles.Count == 0)
        {
            _resourceToProfiles.TryRemove(resourceId, out _);
        }
    }

    private void AddResourceToProfile(int profileId, int resourceId)
    {
        _profileToResources.AddOrUpdate(
            profileId,
            _ => new HashSet<int> { resourceId },
            (_, existing) =>
            {
                var set = existing.ToHashSet();
                set.Add(resourceId);
                return set;
            });
    }

    private void RemoveResourceFromProfile(int profileId, int resourceId)
    {
        _profileToResources.AddOrUpdate(
            profileId,
            _ => new HashSet<int>(),
            (_, existing) =>
            {
                var set = existing.ToHashSet();
                set.Remove(resourceId);
                return set;
            });
    }
}
