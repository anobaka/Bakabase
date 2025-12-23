using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services;

public class PathMarkSyncService<TDbContext> : ScopedService, IPathMarkSyncService
    where TDbContext : DbContext
{
    private readonly IPathMarkService _pathMarkService;
    private readonly IResourceService _resourceService;
    private readonly IMediaLibraryResourceMappingService _mappingService;
    private readonly IMediaLibraryV2Service _mediaLibraryV2Service;
    private readonly ICustomPropertyValueService _customPropertyValueService;
    private readonly IBOptions<ResourceOptions> _resourceOptions;
    private readonly BTaskManager _btm;
    private readonly IBakabaseLocalizer _localizer;
    private readonly IServiceProvider _serviceProvider;

    private const string SyncAllTaskId = "SyncPathMarks";
    private const string SyncImmediateTaskIdPrefix = "SyncPathMark_";

    public PathMarkSyncService(
        IServiceProvider serviceProvider,
        IPathMarkService pathMarkService,
        IResourceService resourceService,
        IMediaLibraryResourceMappingService mappingService,
        IMediaLibraryV2Service mediaLibraryV2Service,
        ICustomPropertyValueService customPropertyValueService,
        IBOptions<ResourceOptions> resourceOptions,
        BTaskManager btm,
        IBakabaseLocalizer localizer) : base(serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _pathMarkService = pathMarkService;
        _resourceService = resourceService;
        _mappingService = mappingService;
        _mediaLibraryV2Service = mediaLibraryV2Service;
        _customPropertyValueService = customPropertyValueService;
        _resourceOptions = resourceOptions;
        _btm = btm;
        _localizer = localizer;
    }

    public async Task StartSyncAll()
    {
        await _btm.Enqueue(new BTaskHandlerBuilder
        {
            Id = SyncAllTaskId,
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any,
            GetName = () => _localizer.BTask_Name("SyncPathMarks"),
            GetDescription = () => _localizer.BTask_Description("SyncPathMarks"),
            GetMessageOnInterruption = () => _localizer.BTask_MessageOnInterruption("SyncPathMarks"),
            ConflictKeys = [SyncAllTaskId],
            Level = BTaskLevel.Default,
            IsPersistent = false,
            StartNow = true,
            DuplicateIdHandling = BTaskDuplicateIdHandling.Reject,
            Run = async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IPathMarkSyncService>();
                var result = await syncService.SyncMarks(
                    null,
                    async p => await args.UpdateTask(t => t.Percentage = p),
                    async p => await args.UpdateTask(t => t.Process = p),
                    args.PauseToken,
                    args.CancellationToken);
                await args.UpdateTask(t => t.Data = result);
            }
        });
    }

    public async Task StartSyncImmediate(int[] markIds)
    {
        var taskId = $"{SyncImmediateTaskIdPrefix}{string.Join("_", markIds)}";

        await _btm.Enqueue(new BTaskHandlerBuilder
        {
            Id = taskId,
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any,
            GetName = () => _localizer.BTask_Name("SyncPathMark_Single"),
            GetDescription = () => _localizer.BTask_Description("SyncPathMark_Single"),
            GetMessageOnInterruption = () => _localizer.BTask_MessageOnInterruption("SyncPathMark_Single"),
            ConflictKeys = [SyncAllTaskId, taskId],
            Level = BTaskLevel.Default,
            IsPersistent = false,
            StartNow = true,
            DuplicateIdHandling = BTaskDuplicateIdHandling.Replace,
            Run = async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IPathMarkSyncService>();
                var result = await syncService.SyncMarks(
                    markIds,
                    async p => await args.UpdateTask(t => t.Percentage = p),
                    async p => await args.UpdateTask(t => t.Process = p),
                    args.PauseToken,
                    args.CancellationToken);
                await args.UpdateTask(t => t.Data = result);
            }
        });
    }

    public async Task<PathMarkSyncResult> SyncMarks(
        int[]? markIds,
        Func<int, Task>? onProgressChange,
        Func<string?, Task>? onProcessChange,
        PauseToken pt,
        CancellationToken ct)
    {
        var result = new PathMarkSyncResult();

        try
        {
            // Phase 1: Collect and sort marks (0-5%)
            await ReportProgress(onProgressChange, onProcessChange, 0, _localizer.SyncPathMark_Collecting());

            List<PathMark> marks;
            if (markIds != null && markIds.Length > 0)
            {
                var allMarks = await _pathMarkService.GetAll();
                marks = allMarks.Where(m => markIds.Contains(m.Id)).ToList();
            }
            else
            {
                marks = await _pathMarkService.GetPendingMarks();
            }

            if (marks.Count == 0)
            {
                await ReportProgress(onProgressChange, onProcessChange, 100, _localizer.SyncPathMark_Complete());
                return result;
            }

            // Separate marks by type
            var resourceMarks = marks.Where(m => m.Type == PathMarkType.Resource).OrderByDescending(m => m.Priority).ToList();
            var propertyMarks = marks.Where(m => m.Type == PathMarkType.Property).OrderByDescending(m => m.Priority).ToList();
            var mediaLibraryMarks = marks.Where(m => m.Type == PathMarkType.MediaLibrary).OrderByDescending(m => m.Priority).ToList();

            await ReportProgress(onProgressChange, onProcessChange, 5, _localizer.SyncPathMark_Collected(marks.Count));

            // Track created resources for property mark processing
            var createdResourcePaths = new List<string>();
            var allResourcePaths = new List<string>();

            // Phase 2: Process Resource Marks (5-40%)
            var resourceMarkCount = resourceMarks.Count;
            for (var i = 0; i < resourceMarkCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var mark = resourceMarks[i];
                var progress = 5 + (int)(35.0 * (i + 1) / resourceMarkCount);

                try
                {
                    await _pathMarkService.MarkAsSyncing(mark.Id);
                    await ReportProgress(onProgressChange, onProcessChange, progress,
                        _localizer.SyncPathMark_ProcessingResource(mark.Path));

                    if (mark.SyncStatus == PathMarkSyncStatus.PendingDelete)
                    {
                        // Handle deletion
                        var deletedCount = await ProcessResourceMarkDelete(mark, ct);
                        result.ResourcesDeleted += deletedCount;
                        await _pathMarkService.HardDelete(mark.Id);
                    }
                    else
                    {
                        // Handle creation/update
                        var (created, paths) = await ProcessResourceMark(mark, ct);
                        result.ResourcesCreated += created;
                        createdResourcePaths.AddRange(paths);
                        allResourcePaths.AddRange(paths);
                        await _pathMarkService.MarkAsSynced(mark.Id);
                    }
                }
                catch (Exception ex)
                {
                    await _pathMarkService.MarkAsFailed(mark.Id, ex.Message);
                    result.FailedMarks++;
                    result.Errors.Add(new PathMarkSyncError
                    {
                        MarkId = mark.Id,
                        Path = mark.Path,
                        ErrorMessage = ex.Message
                    });
                }
            }

            // Phase 3: Find related Property/MediaLibrary Marks (40-45%)
            await ReportProgress(onProgressChange, onProcessChange, 40, _localizer.SyncPathMark_FindingRelated());

            // Find property marks whose Path is a parent directory of any resource path
            var additionalPropertyMarks = await FindRelatedMarks(allResourcePaths, PathMarkType.Property, propertyMarks);
            var allPropertyMarks = propertyMarks.Concat(additionalPropertyMarks).DistinctBy(m => m.Id).OrderByDescending(m => m.Priority).ToList();

            var additionalMediaLibraryMarks = await FindRelatedMarks(allResourcePaths, PathMarkType.MediaLibrary, mediaLibraryMarks);
            var allMediaLibraryMarks = mediaLibraryMarks.Concat(additionalMediaLibraryMarks).DistinctBy(m => m.Id).OrderByDescending(m => m.Priority).ToList();

            await ReportProgress(onProgressChange, onProcessChange, 45, _localizer.SyncPathMark_FoundRelated(additionalPropertyMarks.Count + additionalMediaLibraryMarks.Count));

            // Phase 4 & 5: Process Property Marks (45-80%)
            var propertyMarkCount = allPropertyMarks.Count;
            for (var i = 0; i < propertyMarkCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var mark = allPropertyMarks[i];
                var progress = 45 + (int)(35.0 * (i + 1) / propertyMarkCount);

                try
                {
                    await _pathMarkService.MarkAsSyncing(mark.Id);
                    await ReportProgress(onProgressChange, onProcessChange, progress,
                        _localizer.SyncPathMark_ProcessingProperty(mark.Path));

                    if (mark.SyncStatus == PathMarkSyncStatus.PendingDelete)
                    {
                        // Handle property deletion
                        var deletedCount = await ProcessPropertyMarkDelete(mark, ct);
                        result.PropertiesDeleted += deletedCount;
                        await _pathMarkService.HardDelete(mark.Id);
                    }
                    else
                    {
                        // Handle property application
                        var appliedCount = await ProcessPropertyMark(mark, ct);
                        result.PropertiesApplied += appliedCount;
                        await _pathMarkService.MarkAsSynced(mark.Id);
                    }
                }
                catch (Exception ex)
                {
                    await _pathMarkService.MarkAsFailed(mark.Id, ex.Message);
                    result.FailedMarks++;
                    result.Errors.Add(new PathMarkSyncError
                    {
                        MarkId = mark.Id,
                        Path = mark.Path,
                        ErrorMessage = ex.Message
                    });
                }
            }

            // Phase 6: Process MediaLibrary Marks (80-90%)
            var mediaLibraryMarkCount = allMediaLibraryMarks.Count;
            for (var i = 0; i < mediaLibraryMarkCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var mark = allMediaLibraryMarks[i];
                var progress = 80 + (int)(10.0 * (i + 1) / mediaLibraryMarkCount);

                try
                {
                    await _pathMarkService.MarkAsSyncing(mark.Id);
                    await ReportProgress(onProgressChange, onProcessChange, progress,
                        _localizer.SyncPathMark_ProcessingMediaLibrary(mark.Path));

                    if (mark.SyncStatus == PathMarkSyncStatus.PendingDelete)
                    {
                        var deletedCount = await ProcessMediaLibraryMarkDelete(mark, ct);
                        result.MediaLibraryMappingsDeleted += deletedCount;
                        await _pathMarkService.HardDelete(mark.Id);
                    }
                    else
                    {
                        var createdCount = await ProcessMediaLibraryMark(mark, ct);
                        result.MediaLibraryMappingsCreated += createdCount;
                        await _pathMarkService.MarkAsSynced(mark.Id);
                    }
                }
                catch (Exception ex)
                {
                    await _pathMarkService.MarkAsFailed(mark.Id, ex.Message);
                    result.FailedMarks++;
                    result.Errors.Add(new PathMarkSyncError
                    {
                        MarkId = mark.Id,
                        Path = mark.Path,
                        ErrorMessage = ex.Message
                    });
                }
            }

            // Phase 7: Establish parent-child relationships (90-100%)
            await ReportProgress(onProgressChange, onProcessChange, 90, _localizer.SyncPathMark_EstablishingRelationships());

            if (createdResourcePaths.Count > 0)
            {
                await EstablishParentChildRelationships(createdResourcePaths, ct);
            }

            await _resourceService.RefreshParentTag();

            await ReportProgress(onProgressChange, onProcessChange, 100, _localizer.SyncPathMark_Complete());
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected, don't treat as error
            throw;
        }

        return result;
    }

    public async Task StopSync()
    {
        await _btm.Stop(SyncAllTaskId);
    }

    #region Private Methods

    private async Task ReportProgress(Func<int, Task>? onProgressChange, Func<string?, Task>? onProcessChange, int progress, string? process)
    {
        if (onProgressChange != null) await onProgressChange(progress);
        if (onProcessChange != null) await onProcessChange(process);
    }

    private async Task<(int Created, List<string> Paths)> ProcessResourceMark(PathMark mark, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(mark.ConfigJson);
        if (config == null) return (0, new List<string>());

        var matchedPaths = GetMatchingPathsForResourceMark(mark.Path, config);
        var createdPaths = new List<string>();
        var createdCount = 0;

        // Get existing resources
        var existingResources = await _resourceService.GetAll();
        var existingPathSet = existingResources.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var resourcesToCreate = new List<Resource>();

        foreach (var path in matchedPaths)
        {
            ct.ThrowIfCancellationRequested();

            // Check if resource already exists
            if (existingPathSet.Contains(path))
            {
                createdPaths.Add(path);
                continue;
            }

            // Check for bakabase.json marker
            var existingResourceId = await CheckBakabaseJsonMarker(path);
            if (existingResourceId.HasValue)
            {
                // Resource exists but path changed, update it
                var existingResource = existingResources.FirstOrDefault(r => r.Id == existingResourceId.Value);
                if (existingResource != null)
                {
                    existingResource.Path = path;
                    existingResource.UpdatedAt = DateTime.Now;
                    resourcesToCreate.Add(existingResource);
                    createdPaths.Add(path);
                    continue;
                }
            }

            // Create new resource
            var isFile = File.Exists(path);
            var isDirectory = Directory.Exists(path);

            if (!isFile && !isDirectory) continue;

            DateTime fileCreatedAt, fileModifiedAt;
            if (isFile)
            {
                var fileInfo = new FileInfo(path);
                fileCreatedAt = fileInfo.CreationTime;
                fileModifiedAt = fileInfo.LastWriteTime;
            }
            else
            {
                var dirInfo = new DirectoryInfo(path);
                fileCreatedAt = dirInfo.CreationTime;
                fileModifiedAt = dirInfo.LastWriteTime;
            }

            var resource = new Resource
            {
                Path = path,
                IsFile = isFile,
                CategoryId = 0, // MediaLibraryV2 resource
                MediaLibraryId = 0, // Not associated with any media library yet
                FileCreatedAt = fileCreatedAt,
                FileModifiedAt = fileModifiedAt,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            resourcesToCreate.Add(resource);
            createdPaths.Add(path);
            createdCount++;
        }

        if (resourcesToCreate.Count > 0)
        {
            await _resourceService.AddOrPutRange(resourcesToCreate);

            // Create bakabase.json markers for new folder resources
            if (_resourceOptions.Value.KeepResourcesOnPathChange)
            {
                foreach (var resource in resourcesToCreate.Where(r => !r.IsFile && r.Id > 0))
                {
                    await CreateBakabaseJsonMarker(resource.Path, resource.Id);
                }
            }
        }

        return (createdCount, createdPaths);
    }

    private async Task<int> ProcessResourceMarkDelete(PathMark mark, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(mark.ConfigJson);
        if (config == null) return 0;

        var matchedPaths = GetMatchingPathsForResourceMark(mark.Path, config);
        var existingResources = await _resourceService.GetAll();
        var pathToResourceMap = existingResources.ToDictionary(r => r.Path, r => r, StringComparer.OrdinalIgnoreCase);

        var resourcesToDelete = new List<int>();

        foreach (var path in matchedPaths)
        {
            ct.ThrowIfCancellationRequested();

            if (!pathToResourceMap.TryGetValue(path, out var resource)) continue;

            // Check bakabase.json preservation
            if (_resourceOptions.Value.KeepResourcesOnPathChange && !resource.IsFile)
            {
                var markerPath = Path.Combine(path, InternalOptions.ResourceMarkerFileName);
                if (File.Exists(markerPath))
                {
                    // Don't delete, resource is preserved
                    continue;
                }
            }

            resourcesToDelete.Add(resource.Id);
        }

        if (resourcesToDelete.Count > 0)
        {
            await _resourceService.DeleteByKeys(resourcesToDelete.ToArray(), deleteFiles: false);
        }

        return resourcesToDelete.Count;
    }

    private async Task<int> ProcessPropertyMark(PathMark mark, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(mark.ConfigJson);
        if (config == null) return 0;

        // Get all resources and find those whose paths are under the mark's path
        var allResources = await _resourceService.GetAll();
        var matchedResources = allResources
            .Where(r => IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        if (matchedResources.Count == 0) return 0;

        // Filter resources based on mark's match config
        var filteredResources = FilterResourcesByMarkConfig(matchedResources, mark.Path, config);

        var appliedCount = 0;

        foreach (var resource in filteredResources)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var propertyValue = ExtractPropertyValue(resource.Path, mark.Path, config);
                if (propertyValue == null) continue;

                // Apply property value to resource
                await ApplyPropertyValue(resource, config, propertyValue);
                appliedCount++;
            }
            catch
            {
                // Skip individual property errors
            }
        }

        return appliedCount;
    }

    private async Task<int> ProcessPropertyMarkDelete(PathMark mark, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(mark.ConfigJson);
        if (config == null) return 0;

        // Find resources that had this property applied
        var allResources = await _resourceService.GetAll();
        var matchedResources = allResources
            .Where(r => IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        var filteredResources = FilterResourcesByMarkConfig(matchedResources, mark.Path, config);

        var deletedCount = 0;

        foreach (var resource in filteredResources)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Remove property value with Synchronization scope
                await RemovePropertyValue(resource, config);
                deletedCount++;
            }
            catch
            {
                // Skip individual property errors
            }
        }

        return deletedCount;
    }

    private async Task<int> ProcessMediaLibraryMark(PathMark mark, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(mark.ConfigJson);
        if (config == null) return 0;

        // Get all resources under the mark's path
        var allResources = await _resourceService.GetAll();
        var matchedResources = allResources
            .Where(r => IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        var filteredResources = FilterResourcesByMediaLibraryMarkConfig(matchedResources, mark.Path, config);

        var createdCount = 0;

        // Cache for dynamic mode: media library name -> id
        var mediaLibraryCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Pre-load existing media libraries for dynamic mode
        if (config.ValueType == PropertyValueType.Dynamic)
        {
            var existingLibraries = await _mediaLibraryV2Service.GetAll();
            foreach (var lib in existingLibraries)
            {
                if (!string.IsNullOrEmpty(lib.Name) && !mediaLibraryCache.ContainsKey(lib.Name))
                {
                    mediaLibraryCache[lib.Name] = lib.Id;
                }
            }
        }

        foreach (var resource in filteredResources)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                int mediaLibraryId;

                if (config.ValueType == PropertyValueType.Fixed)
                {
                    // Fixed mode: use the configured media library ID
                    if (!config.MediaLibraryId.HasValue) continue;
                    mediaLibraryId = config.MediaLibraryId.Value;
                }
                else
                {
                    // Dynamic mode: extract media library name from path
                    var mediaLibraryName = ExtractMediaLibraryName(resource.Path, mark.Path, config);
                    if (string.IsNullOrEmpty(mediaLibraryName)) continue;

                    // Check cache first
                    if (!mediaLibraryCache.TryGetValue(mediaLibraryName, out mediaLibraryId))
                    {
                        // Create new media library
                        var newLibrary = await _mediaLibraryV2Service.Add(new MediaLibraryV2AddOrPutInputModel(
                            Name: mediaLibraryName,
                            Paths: new List<string>()
                        ));
                        mediaLibraryId = newLibrary.Id;
                        mediaLibraryCache[mediaLibraryName] = mediaLibraryId;
                    }
                }

                await _mappingService.EnsureMappings(
                    resource.Id,
                    new[] { mediaLibraryId });
                createdCount++;
            }
            catch
            {
                // Skip individual mapping errors
            }
        }

        return createdCount;
    }

    private string? ExtractMediaLibraryName(string resourcePath, string markPath, MediaLibraryMarkConfig config)
    {
        var normalizedResourcePath = resourcePath.StandardizePath()!;
        var normalizedMarkPath = markPath.StandardizePath()!;

        var resourceSegments = normalizedResourcePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        var markSegments = normalizedMarkPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        // Calculate the target index based on LayerToMediaLibrary
        var layer = config.LayerToMediaLibrary ?? 0;
        int targetIndex;

        if (layer == 0)
        {
            // Layer 0 = matched item itself
            targetIndex = resourceSegments.Length - 1;
        }
        else
        {
            // Layer > 0 = parent directories
            targetIndex = resourceSegments.Length - 1 - layer;
        }

        if (targetIndex < 0 || targetIndex >= resourceSegments.Length)
        {
            return null;
        }

        var directoryName = resourceSegments[targetIndex];

        // Apply regex extraction if configured
        if (!string.IsNullOrEmpty(config.RegexToMediaLibrary))
        {
            try
            {
                var regex = new Regex(config.RegexToMediaLibrary, RegexOptions.IgnoreCase);
                var match = regex.Match(directoryName);
                if (match.Success)
                {
                    // Return first capture group if available, otherwise full match
                    return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                }
                return null; // Regex didn't match
            }
            catch
            {
                // Invalid regex, return directory name as-is
                return directoryName;
            }
        }

        return directoryName;
    }

    private async Task<int> ProcessMediaLibraryMarkDelete(PathMark mark, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(mark.ConfigJson);
        if (config == null) return 0;

        // Get all resources under the mark's path
        var allResources = await _resourceService.GetAll();
        var matchedResources = allResources
            .Where(r => IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        var filteredResources = FilterResourcesByMediaLibraryMarkConfig(matchedResources, mark.Path, config);
        var deletedCount = 0;

        foreach (var resource in filteredResources)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await _mappingService.DeleteByResourceId(resource.Id);
                deletedCount++;
            }
            catch
            {
                // Skip individual deletion errors
            }
        }

        return deletedCount;
    }

    private async Task<List<PathMark>> FindRelatedMarks(List<string> resourcePaths, PathMarkType type, List<PathMark> excludeMarks)
    {
        var excludeIds = excludeMarks.Select(m => m.Id).ToHashSet();
        var allMarks = await _pathMarkService.GetAll(m => m.Type == type && !excludeIds.Contains(m.Id));

        // Find marks whose Path is a parent directory of any resource path
        // Include Synced marks so they can be re-applied to newly created resources
        var relatedMarks = allMarks
            .Where(m => resourcePaths.Any(rp => IsPathUnderParent(rp, m.Path)))
            .Where(m => m.SyncStatus is PathMarkSyncStatus.Pending or PathMarkSyncStatus.PendingDelete or PathMarkSyncStatus.Synced)
            .ToList();

        return relatedMarks;
    }

    private bool IsPathUnderParent(string childPath, string parentPath)
    {
        var normalizedChild = childPath.StandardizePath();
        var normalizedParent = parentPath.StandardizePath();

        if (string.IsNullOrEmpty(normalizedChild) || string.IsNullOrEmpty(normalizedParent))
            return false;

        return normalizedChild.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedChild.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private async Task EstablishParentChildRelationships(List<string> resourcePaths, CancellationToken ct)
    {
        var allResources = await _resourceService.GetAll();
        var pathToResourceMap = allResources.ToDictionary(r => r.Path, r => r, StringComparer.OrdinalIgnoreCase);

        // Standardize and sort paths lexicographically
        // After sorting, parent paths will appear before their child paths
        var sortedPaths = resourcePaths
            .Select(p => p.StandardizePath()!)
            .Where(p => !string.IsNullOrEmpty(p) && pathToResourceMap.ContainsKey(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var changedResources = new List<Resource>();

        // Stack to track potential parent paths
        // Each entry is (path, resource)
        var parentStack = new Stack<(string Path, Resource Resource)>();

        foreach (var path in sortedPaths)
        {
            ct.ThrowIfCancellationRequested();

            var resource = pathToResourceMap[path];

            // Pop from stack until we find a valid parent or stack is empty
            // A valid parent's path + separator must be a prefix of current path
            while (parentStack.Count > 0)
            {
                var top = parentStack.Peek();
                var parentPathWithSeparator = top.Path + Path.DirectorySeparatorChar;

                if (path.StartsWith(parentPathWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    // Found parent
                    break;
                }

                // Not a parent, pop and continue
                parentStack.Pop();
            }

            // Set parent if found
            if (parentStack.Count > 0)
            {
                var parent = parentStack.Peek();
                if (resource.ParentId != parent.Resource.Id)
                {
                    resource.ParentId = parent.Resource.Id;
                    changedResources.Add(resource);
                }
            }
            else if (resource.ParentId != null)
            {
                // No parent found but resource had one, clear it
                resource.ParentId = null;
                changedResources.Add(resource);
            }

            // Push current path as potential parent for subsequent paths
            parentStack.Push((path, resource));
        }

        if (changedResources.Count > 0)
        {
            await _resourceService.AddOrPutRange(changedResources);
        }
    }

    private List<string> GetMatchingPathsForResourceMark(string rootPath, ResourceMarkConfig config)
    {
        var matchedPaths = new List<string>();
        var normalizedRoot = rootPath.StandardizePath()!;

        if (!Directory.Exists(normalizedRoot)) return matchedPaths;

        try
        {
            List<string> initialMatches;

            if (config.MatchMode == PathMatchMode.Layer)
            {
                if (config.Layer == null) return matchedPaths;

                var layer = config.Layer.Value;
                if (layer < 0)
                {
                    // Negative layer means parent directories
                    // -1 = parent, -2 = grandparent, etc.
                    initialMatches = GetParentAtLayer(normalizedRoot, Math.Abs(layer), config.FsTypeFilter);
                }
                else
                {
                    initialMatches = GetEntriesAtLayer(normalizedRoot, layer, config.FsTypeFilter, config.Extensions);
                }
            }
            else if (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.Regex))
            {
                var regex = new Regex(config.Regex, RegexOptions.IgnoreCase);
                var entries = GetAllEntries(normalizedRoot, config.FsTypeFilter, config.Extensions);
                initialMatches = new List<string>();

                foreach (var entry in entries)
                {
                    var relativePath = entry.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (regex.IsMatch(relativePath))
                    {
                        initialMatches.Add(entry);
                    }
                }
            }
            else
            {
                return matchedPaths;
            }

            // Apply ApplyScope logic
            if (config.ApplyScope == PathMarkApplyScope.MatchedOnly)
            {
                // Only matched paths
                matchedPaths.AddRange(initialMatches);
            }
            else if (config.ApplyScope == PathMarkApplyScope.MatchedAndSubdirectories)
            {
                // Matched paths + all subdirectories
                matchedPaths.AddRange(initialMatches);
                foreach (var match in initialMatches)
                {
                    if (Directory.Exists(match))
                    {
                        var subdirEntries = GetAllEntries(match, config.FsTypeFilter, config.Extensions);
                        matchedPaths.AddRange(subdirEntries);
                    }
                }
            }
        }
        catch
        {
            // Ignore access errors
        }

        return matchedPaths;
    }

    private List<string> GetAllEntries(string rootPath, PathFilterFsType? fsTypeFilter, List<string>? extensions)
    {
        var entries = new List<string>();

        try
        {
            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.Directory)
            {
                entries.AddRange(Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories));
            }

            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File)
            {
                var files = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
                if (extensions != null && extensions.Count > 0)
                {
                    files = files.Where(f => extensions.Any(ext =>
                        f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToArray();
                }
                entries.AddRange(files);
            }
        }
        catch
        {
            // Ignore access errors
        }

        return entries.Select(x => x.StandardizePath()!).ToList();
    }

    private List<string> GetEntriesAtLayer(string rootPath, int layer, PathFilterFsType? fsTypeFilter, List<string>? extensions)
    {
        var entries = new List<string>();

        try
        {
            var currentPaths = new List<string> { rootPath };

            for (int i = 1; i <= layer; i++)
            {
                var nextPaths = new List<string>();
                foreach (var path in currentPaths)
                {
                    try
                    {
                        nextPaths.AddRange(Directory.GetDirectories(path));
                        if (i == layer && (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File))
                        {
                            var files = Directory.GetFiles(path);
                            if (extensions != null && extensions.Count > 0)
                            {
                                files = files.Where(f => extensions.Any(ext =>
                                    f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToArray();
                            }
                            nextPaths.AddRange(files);
                        }
                    }
                    catch
                    {
                        // Ignore access errors
                    }
                }
                currentPaths = nextPaths;
            }

            if (fsTypeFilter == PathFilterFsType.Directory)
            {
                entries.AddRange(currentPaths.Where(Directory.Exists));
            }
            else if (fsTypeFilter == PathFilterFsType.File)
            {
                entries.AddRange(currentPaths.Where(File.Exists));
            }
            else
            {
                entries.AddRange(currentPaths);
            }
        }
        catch
        {
            // Ignore access errors
        }

        return entries.Select(x => x.StandardizePath()!).ToList();
    }

    private List<string> GetParentAtLayer(string rootPath, int absLayer, PathFilterFsType? fsTypeFilter)
    {
        var entries = new List<string>();

        try
        {
            // Navigate up to parent directories
            var currentPath = rootPath;
            for (int i = 0; i < absLayer; i++)
            {
                var parentPath = Path.GetDirectoryName(currentPath);
                if (string.IsNullOrEmpty(parentPath))
                {
                    // Reached root, can't go higher
                    return entries;
                }
                currentPath = parentPath;
            }

            // Check if the parent exists and matches filter
            if (Directory.Exists(currentPath))
            {
                if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.Directory)
                {
                    entries.Add(currentPath.StandardizePath()!);
                }
            }
            else if (File.Exists(currentPath))
            {
                if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File)
                {
                    entries.Add(currentPath.StandardizePath()!);
                }
            }
        }
        catch
        {
            // Ignore access errors
        }

        return entries;
    }

    private List<Resource> FilterResourcesByMarkConfig(List<Resource> resources, string markPath, PropertyMarkConfig config)
    {
        var normalizedMarkPath = markPath.StandardizePath()!;

        // First filter by MatchMode
        var filteredResources = config.MatchMode switch
        {
            PathMatchMode.Layer when config.Layer.HasValue => FilterByLayer(resources, normalizedMarkPath, config.Layer.Value),
            PathMatchMode.Regex when !string.IsNullOrEmpty(config.Regex) => FilterByRegex(resources, normalizedMarkPath, config.Regex),
            _ => resources
        };

        // Then apply ApplyScope logic
        if (config.ApplyScope == PathMarkApplyScope.MatchedOnly)
        {
            // Only return the matched resources
            return filteredResources;
        }
        else if (config.ApplyScope == PathMarkApplyScope.MatchedAndSubdirectories)
        {
            // Return matched resources + all resources under them
            var result = new List<Resource>(filteredResources);
            var matchedPaths = filteredResources.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var resource in resources)
            {
                if (matchedPaths.Contains(resource.Path)) continue; // Already included

                // Check if this resource is under any matched path
                if (matchedPaths.Any(matchedPath => IsPathUnderParent(resource.Path, matchedPath) &&
                                                     !resource.Path.Equals(matchedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(resource);
                }
            }

            return result;
        }

        return filteredResources;
    }

    private List<Resource> FilterResourcesByMediaLibraryMarkConfig(List<Resource> resources, string markPath, MediaLibraryMarkConfig config)
    {
        var normalizedMarkPath = markPath.StandardizePath()!;

        // First filter by MatchMode
        var filteredResources = config.MatchMode switch
        {
            PathMatchMode.Layer when config.Layer.HasValue => FilterByLayer(resources, normalizedMarkPath, config.Layer.Value),
            PathMatchMode.Regex when !string.IsNullOrEmpty(config.Regex) => FilterByRegex(resources, normalizedMarkPath, config.Regex),
            _ => resources
        };

        // Then apply ApplyScope logic
        if (config.ApplyScope == PathMarkApplyScope.MatchedOnly)
        {
            // Only return the matched resources
            return filteredResources;
        }
        else if (config.ApplyScope == PathMarkApplyScope.MatchedAndSubdirectories)
        {
            // Return matched resources + all resources under them
            var result = new List<Resource>(filteredResources);
            var matchedPaths = filteredResources.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var resource in resources)
            {
                if (matchedPaths.Contains(resource.Path)) continue; // Already included

                // Check if this resource is under any matched path
                if (matchedPaths.Any(matchedPath => IsPathUnderParent(resource.Path, matchedPath) &&
                                                     !resource.Path.Equals(matchedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(resource);
                }
            }

            return result;
        }

        return filteredResources;
    }

    private List<Resource> FilterByLayer(List<Resource> resources, string rootPath, int layer)
    {
        var rootSegments = rootPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        if (layer < 0)
        {
            // Negative layer means parent directories
            // -1 = parent, -2 = grandparent, etc.
            // For resources, filter those whose path is the parent at the specified level
            var absLayer = Math.Abs(layer);
            var targetSegmentCount = rootSegments.Length - absLayer;
            if (targetSegmentCount <= 0) return new List<Resource>();

            return resources.Where(r =>
            {
                var resourceSegments = r.Path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
                return resourceSegments.Length == targetSegmentCount;
            }).ToList();
        }

        return resources.Where(r =>
        {
            var resourceSegments = r.Path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            var relativeDepth = resourceSegments.Length - rootSegments.Length;
            return relativeDepth == layer;
        }).ToList();
    }

    private List<Resource> FilterByRegex(List<Resource> resources, string rootPath, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        return resources.Where(r =>
        {
            var relativePath = r.Path.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return regex.IsMatch(relativePath);
        }).ToList();
    }

    private object? ExtractPropertyValue(string resourcePath, string markPath, PropertyMarkConfig config)
    {
        if (config.ValueType == PropertyValueType.Fixed)
        {
            return config.FixedValue;
        }

        // Dynamic value extraction
        var normalizedResourcePath = resourcePath.StandardizePath()!;
        var normalizedMarkPath = markPath.StandardizePath()!;

        var resourceSegments = normalizedResourcePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        var markSegments = normalizedMarkPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        if (config.MatchMode == PathMatchMode.Layer && config.ValueLayer.HasValue)
        {
            var valueLayer = config.ValueLayer.Value;
            if (valueLayer == 0)
            {
                // The matched item itself
                return Path.GetFileName(normalizedResourcePath);
            }

            var targetIndex = markSegments.Length + valueLayer - 1;
            if (targetIndex >= 0 && targetIndex < resourceSegments.Length)
            {
                return resourceSegments[targetIndex];
            }
        }
        else if (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.ValueRegex))
        {
            var relativePath = normalizedResourcePath.Substring(normalizedMarkPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var regex = new Regex(config.ValueRegex, RegexOptions.IgnoreCase);
            var match = regex.Match(relativePath);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }
            else if (match.Success)
            {
                return match.Value;
            }
        }

        return null;
    }

    private async Task ApplyPropertyValue(Resource resource, PropertyMarkConfig config, object? value)
    {
        if (value == null) return;

        // Only support Custom properties for now (Pool.Custom)
        if (config.Pool != PropertyPool.Custom) return;

        // Serialize value to string for storage
        var valueString = value is string s ? s : System.Text.Json.JsonSerializer.Serialize(value);

        // Check if value already exists
        var existingValue = (await _customPropertyValueService.GetAllDbModels(x =>
            x.ResourceId == resource.Id &&
            x.PropertyId == config.PropertyId &&
            x.Scope == (int)PropertyValueScope.Synchronization)).FirstOrDefault();

        if (existingValue == null)
        {
            var newValue = new CustomPropertyValueDbModel
            {
                ResourceId = resource.Id,
                PropertyId = config.PropertyId,
                Value = valueString,
                Scope = (int)PropertyValueScope.Synchronization
            };
            await _customPropertyValueService.AddDbModel(newValue);
        }
        else
        {
            existingValue.Value = valueString;
            await _customPropertyValueService.UpdateDbModel(existingValue);
        }
    }

    private async Task RemovePropertyValue(Resource resource, PropertyMarkConfig config)
    {
        // Only support Custom properties for now (Pool.Custom)
        if (config.Pool != PropertyPool.Custom) return;

        // Remove Synchronization scope values
        await _customPropertyValueService.RemoveAll(x =>
            x.ResourceId == resource.Id &&
            x.PropertyId == config.PropertyId &&
            x.Scope == (int)PropertyValueScope.Synchronization);
    }

    private async Task<int?> CheckBakabaseJsonMarker(string path)
    {
        if (!_resourceOptions.Value.KeepResourcesOnPathChange) return null;
        if (File.Exists(path)) return null; // Only for directories

        var markerPath = Path.Combine(path, InternalOptions.ResourceMarkerFileName);
        if (!File.Exists(markerPath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(markerPath);
            var markerData = System.Text.Json.JsonDocument.Parse(json);
            if (markerData.RootElement.TryGetProperty("ids", out var idsElement))
            {
                var ids = idsElement.EnumerateArray().Select(e => e.GetInt32()).ToArray();
                return ids.FirstOrDefault();
            }
        }
        catch
        {
            // Ignore marker read errors
        }

        return null;
    }

    private async Task CreateBakabaseJsonMarker(string path, int resourceId)
    {
        try
        {
            var markerPath = Path.Combine(path, InternalOptions.ResourceMarkerFileName);
            var content = System.Text.Json.JsonSerializer.Serialize(new { ids = new[] { resourceId } });
            await File.WriteAllTextAsync(markerPath, content);
            File.SetAttributes(markerPath, File.GetAttributes(markerPath) | FileAttributes.Hidden);
        }
        catch
        {
            // Ignore marker creation errors
        }
    }

    #endregion
}
