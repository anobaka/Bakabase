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
            // Task is already running or queued, it will pick up pending invalidations
            return;
        }

        var builder = new BTaskHandlerBuilder
        {
            Id = TaskId,
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any,
            GetName = () => _localizer.BTask_Name("ResourceProfileIndex"),
            GetDescription = () => _localizer.BTask_Description("ResourceProfileIndex"),
            GetMessageOnInterruption = null,
            CancellationToken = null,
            Run = ProcessPendingUpdates,
            ConflictKeys = [TaskId],
            Level = BTaskLevel.Default,
            IsPersistent = false,
            StartNow = true,
            DuplicateIdHandling = BTaskDuplicateIdHandling.Ignore
        };

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

            // Check if full rebuild is needed
            if (_pendingFullRebuild || !_isReady)
            {
                _pendingFullRebuild = false;
                // Clear partial invalidation queues since we're doing full rebuild
                while (_pendingResourceInvalidations.TryDequeue(out _)) { }
                while (_pendingProfileInvalidations.TryDequeue(out _)) { }

                await FullRebuild(resourceProfileService, resourceService, args);
                return;
            }

            // Process profile invalidations first (they may require re-evaluating all resources)
            var profilesToInvalidate = new HashSet<int>();
            while (_pendingProfileInvalidations.TryDequeue(out var profileId))
            {
                profilesToInvalidate.Add(profileId);
            }

            if (profilesToInvalidate.Count > 0)
            {
                await ProcessProfileInvalidations(profilesToInvalidate, resourceProfileService, resourceService, args);
            }

            // Process resource invalidations
            var resourcesToInvalidate = new HashSet<int>();
            while (_pendingResourceInvalidations.TryDequeue(out var resourceId))
            {
                resourcesToInvalidate.Add(resourceId);
            }

            if (resourcesToInvalidate.Count > 0)
            {
                await ProcessResourceInvalidations(resourcesToInvalidate, resourceProfileService, args);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ResourceProfile index updates");
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
        BTaskArgs args)
    {
        _logger.LogDebug("Processing {Count} profile invalidations", profileIds.Count);

        // Get current profiles
        var allProfiles = await resourceProfileService.GetAll();
        var profileMap = allProfiles.ToDictionary(p => p.Id);

        // Update priorities
        foreach (var profile in allProfiles)
        {
            _profilePriorities[profile.Id] = profile.Priority;
        }

        foreach (var profileId in profileIds)
        {
            args.CancellationToken.ThrowIfCancellationRequested();

            // Remove old entries for this profile
            if (_profileToResources.TryRemove(profileId, out var oldResourceIds))
            {
                foreach (var resourceId in oldResourceIds)
                {
                    RemoveProfileFromResource(resourceId, profileId);
                }
            }

            // If profile still exists, rebuild its index
            if (profileMap.TryGetValue(profileId, out var profile))
            {
                var matchingResourceIds = await resourceProfileService.GetMatchingResourceIds(profileId);
                _profileToResources[profileId] = matchingResourceIds;

                foreach (var resourceId in matchingResourceIds)
                {
                    AddProfileToResource(resourceId, profileId);
                }
            }
            else
            {
                // Profile was deleted, remove from priority cache
                _profilePriorities.TryRemove(profileId, out _);
            }
        }
    }

    private async Task ProcessResourceInvalidations(
        HashSet<int> resourceIds,
        IResourceProfileService resourceProfileService,
        BTaskArgs args)
    {
        _logger.LogDebug("Processing {Count} resource invalidations", resourceIds.Count);

        var allProfiles = await resourceProfileService.GetAll();

        foreach (var resourceId in resourceIds)
        {
            args.CancellationToken.ThrowIfCancellationRequested();

            // Clear existing mappings for this resource
            if (_resourceToProfiles.TryRemove(resourceId, out var oldProfileIds))
            {
                foreach (var profileId in oldProfileIds)
                {
                    RemoveResourceFromProfile(profileId, resourceId);
                }
            }

            // Re-evaluate against all profiles
            var matchingProfileIds = new List<int>();
            foreach (var profile in allProfiles)
            {
                if (_profileToResources.TryGetValue(profile.Id, out var profileResourceIds))
                {
                    // Check if we need to re-evaluate
                    var newMatchingIds = await resourceProfileService.GetMatchingResourceIds(profile.Id);
                    if (newMatchingIds.Contains(resourceId))
                    {
                        matchingProfileIds.Add(profile.Id);
                        AddResourceToProfile(profile.Id, resourceId);
                    }
                }
                else
                {
                    // Profile has no cached resources, evaluate
                    var matchingIds = await resourceProfileService.GetMatchingResourceIds(profile.Id);
                    _profileToResources[profile.Id] = matchingIds;
                    if (matchingIds.Contains(resourceId))
                    {
                        matchingProfileIds.Add(profile.Id);
                    }
                }
            }

            if (matchingProfileIds.Count > 0)
            {
                // Sort by priority
                matchingProfileIds.Sort((a, b) =>
                    _profilePriorities.GetValueOrDefault(b, 0)
                        .CompareTo(_profilePriorities.GetValueOrDefault(a, 0)));
                _resourceToProfiles[resourceId] = matchingProfileIds;
            }
        }
    }

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
