using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Bakabase.Abstractions.Models.Domain;
using DomainResource = Bakabase.Abstractions.Models.Domain.Resource;
using DomainPathRule = Bakabase.Abstractions.Models.Domain.PathRule;

namespace Bakabase.InsideWorld.Business.Components.PathRule;

/// <summary>
/// Background service that processes PathRule queue items.
/// Handles applying path rules to discover and create resources.
/// </summary>
public class PathRuleQueueProcessor : BackgroundService
{
    private readonly ILogger<PathRuleQueueProcessor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);
    private readonly int _batchSize = 50;

    public PathRuleQueueProcessor(
        ILogger<PathRuleQueueProcessor> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PathRuleQueueProcessor started");

        // Wait a bit for the application to fully start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing PathRule queue");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("PathRuleQueueProcessor stopped");
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var queueService = scope.ServiceProvider.GetRequiredService<IPathRuleQueueService>();
        var pathRuleService = scope.ServiceProvider.GetRequiredService<IPathRuleService>();
        var resourceService = scope.ServiceProvider.GetRequiredService<IResourceService>();
        var mappingService = scope.ServiceProvider.GetRequiredService<IMediaLibraryResourceMappingService>();
        var mediaLibraryService = scope.ServiceProvider.GetRequiredService<IMediaLibraryV2Service>();

        var pendingItems = await queueService.GetPendingItems(_batchSize);

        if (!pendingItems.Any())
        {
            return;
        }

        _logger.LogInformation("Processing {Count} queue items", pendingItems.Count);

        foreach (var item in pendingItems)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await queueService.MarkAsProcessing(item.Id);

                switch (item.Action)
                {
                    case RuleQueueAction.Apply:
                        await ProcessApplyAction(item, pathRuleService, resourceService, mappingService, mediaLibraryService, ct);
                        break;

                    case RuleQueueAction.Reevaluate:
                        await ProcessReevaluateAction(item, pathRuleService, resourceService, mappingService, mediaLibraryService, ct);
                        break;

                    case RuleQueueAction.FileSystemChange:
                        await ProcessFileSystemChangeAction(item, pathRuleService, resourceService, mappingService, mediaLibraryService, ct);
                        break;

                    default:
                        _logger.LogWarning("Unknown action type: {Action}", item.Action);
                        break;
                }

                await queueService.MarkAsCompleted(item.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queue item {Id}", item.Id);
                await queueService.MarkAsFailed(item.Id, ex.Message);
            }
        }
    }

    /// <summary>
    /// Apply a path rule to discover resources in a directory.
    /// </summary>
    private async Task ProcessApplyAction(
        PathRuleQueueItem item,
        IPathRuleService pathRuleService,
        IResourceService resourceService,
        IMediaLibraryResourceMappingService mappingService,
        IMediaLibraryV2Service mediaLibraryService,
        CancellationToken ct)
    {
        if (!item.RuleId.HasValue)
        {
            _logger.LogWarning("Apply action requires RuleId");
            return;
        }

        var rule = await pathRuleService.Get(item.RuleId.Value);
        if (rule == null)
        {
            _logger.LogWarning("Rule {RuleId} not found", item.RuleId.Value);
            return;
        }

        var matchedPaths = await pathRuleService.PreviewMatchedPaths(rule);
        _logger.LogInformation("Rule {RuleId} matched {Count} paths", rule.Id, matchedPaths.Count);

        foreach (var path in matchedPaths)
        {
            if (ct.IsCancellationRequested) break;

            await CreateOrUpdateResourceFromPath(path, rule, resourceService, mappingService, mediaLibraryService);
        }
    }

    /// <summary>
    /// Re-evaluate resources against all applicable rules.
    /// </summary>
    private async Task ProcessReevaluateAction(
        PathRuleQueueItem item,
        IPathRuleService pathRuleService,
        IResourceService resourceService,
        IMediaLibraryResourceMappingService mappingService,
        IMediaLibraryV2Service mediaLibraryService,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(item.Path))
        {
            _logger.LogWarning("Reevaluate action requires Path");
            return;
        }

        var applicableRules = await pathRuleService.GetApplicableRules(item.Path);
        if (!applicableRules.Any())
        {
            _logger.LogInformation("No applicable rules for path: {Path}", item.Path);
            return;
        }

        // Use the most specific rule (longest path match)
        var rule = applicableRules.First();

        // Check if this path matches the rule's resource filter
        if (IsPathMatchedByRule(item.Path, rule))
        {
            await CreateOrUpdateResourceFromPath(item.Path, rule, resourceService, mappingService, mediaLibraryService);
        }
    }

    /// <summary>
    /// Handle file system changes (create, delete, rename).
    /// </summary>
    private async Task ProcessFileSystemChangeAction(
        PathRuleQueueItem item,
        IPathRuleService pathRuleService,
        IResourceService resourceService,
        IMediaLibraryResourceMappingService mappingService,
        IMediaLibraryV2Service mediaLibraryService,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(item.Path))
        {
            _logger.LogWarning("FileSystemChange action requires Path");
            return;
        }

        var applicableRules = await pathRuleService.GetApplicableRules(item.Path);
        if (!applicableRules.Any())
        {
            return;
        }

        var rule = applicableRules.First();

        // Check if path exists (for create/modify) or not (for delete)
        var pathExists = Directory.Exists(item.Path) || File.Exists(item.Path);

        if (pathExists && IsPathMatchedByRule(item.Path, rule))
        {
            await CreateOrUpdateResourceFromPath(item.Path, rule, resourceService, mappingService, mediaLibraryService);
        }
        else if (!pathExists)
        {
            // Path was deleted - find and remove the resource
            var existingResources = await resourceService.GetAll(r => r.Path == item.Path);
            if (existingResources.Any())
            {
                var resourceIds = existingResources.Select(r => r.Id).ToArray();
                _logger.LogInformation("Removing {Count} resources for deleted path: {Path}", resourceIds.Length, item.Path);
                await resourceService.DeleteByKeys(resourceIds, false);
            }
        }
    }

    /// <summary>
    /// Create or update a resource from a matched path.
    /// </summary>
    private async Task CreateOrUpdateResourceFromPath(
        string path,
        DomainPathRule rule,
        IResourceService resourceService,
        IMediaLibraryResourceMappingService mappingService,
        IMediaLibraryV2Service mediaLibraryService)
    {
        var normalizedPath = path.StandardizePath()!;

        // Check if resource already exists
        var existingResources = await resourceService.GetAll(r => r.Path == normalizedPath);
        var existingResource = existingResources.FirstOrDefault();

        var isFile = File.Exists(normalizedPath);

        if (existingResource != null)
        {
            _logger.LogDebug("Resource already exists for path: {Path}", normalizedPath);
            return;
        }

        // Get MediaLibraryId from the MediaLibraryV2 that contains this path
        var mediaLibraryId = await GetMediaLibraryIdForPath(normalizedPath, mediaLibraryService);
        if (!mediaLibraryId.HasValue)
        {
            _logger.LogWarning("Could not determine MediaLibraryId for path: {Path}", normalizedPath);
            return;
        }

        var fileInfo = isFile ? new FileInfo(normalizedPath) : null;
        var dirInfo = !isFile ? new DirectoryInfo(normalizedPath) : null;

        var resource = new DomainResource
        {
            Path = normalizedPath,
            IsFile = isFile,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            FileCreatedAt = fileInfo?.CreationTimeUtc ?? dirInfo?.CreationTimeUtc ?? DateTime.UtcNow,
            FileModifiedAt = fileInfo?.LastWriteTimeUtc ?? dirInfo?.LastWriteTimeUtc ?? DateTime.UtcNow,
            MediaLibraryId = mediaLibraryId.Value,
            CategoryId = 0, // Will be set by the service based on MediaLibrary
            Tags = new HashSet<ResourceTag>()
        };

        var results = await resourceService.AddOrPutRange(new List<DomainResource> { resource });
        _logger.LogInformation("Created resource for path: {Path}, results: {Results}", normalizedPath, results.Count);

        // Create mapping for new resources
        var addedResources = await resourceService.GetAll(r => r.Path == normalizedPath);
        var addedResource = addedResources.FirstOrDefault();
        if (addedResource != null)
        {
            await mappingService.EnsureMappings(addedResource.Id, new[] { mediaLibraryId.Value }, MappingSource.Rule);
        }
    }

    /// <summary>
    /// Check if a path is matched by a rule's resource filters.
    /// </summary>
    private bool IsPathMatchedByRule(string path, DomainPathRule rule)
    {
        if (rule.Marks == null || !rule.Marks.Any())
        {
            return false;
        }

        var resourceMarks = rule.Marks
            .Where(m => m.Type == PathMarkType.Resource)
            .OrderBy(m => m.Priority)
            .ToList();

        if (!resourceMarks.Any())
        {
            return false;
        }

        var normalizedPath = path.StandardizePath()!;
        var normalizedRulePath = rule.Path.StandardizePath()!;

        if (!normalizedPath.StartsWith(normalizedRulePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relativePath = normalizedPath.Substring(normalizedRulePath.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var mark in resourceMarks)
        {
            var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(mark.ConfigJson);
            if (config == null) continue;

            if (MatchesResourceConfig(relativePath, normalizedPath, config))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesResourceConfig(string relativePath, string fullPath, ResourceMarkConfig config)
    {
        var isFile = File.Exists(fullPath);
        var isDirectory = Directory.Exists(fullPath);

        // Check FS type filter
        if (config.FsTypeFilter.HasValue)
        {
            if (config.FsTypeFilter == PathFilterFsType.File && !isFile) return false;
            if (config.FsTypeFilter == PathFilterFsType.Directory && !isDirectory) return false;
        }

        // Check extensions
        if (config.Extensions != null && config.Extensions.Any() && isFile)
        {
            var ext = Path.GetExtension(fullPath);
            if (!config.Extensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        // Check match mode
        if (config.MatchMode == PathMatchMode.Layer)
        {
            if (!config.Layer.HasValue) return false;

            var segments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            var layer = segments.Length;

            if (config.Layer == -1) // All layers
            {
                return layer > 0;
            }

            return layer == config.Layer.Value;
        }
        else if (config.MatchMode == PathMatchMode.Regex)
        {
            if (string.IsNullOrEmpty(config.Regex)) return false;

            try
            {
                var regex = new Regex(config.Regex, RegexOptions.IgnoreCase);
                return regex.IsMatch(relativePath);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Get the MediaLibraryId for a given path by looking up which MediaLibraryV2 contains this path.
    /// </summary>
    private async Task<int?> GetMediaLibraryIdForPath(string path, IMediaLibraryV2Service mediaLibraryService)
    {
        var normalizedPath = path.StandardizePath()!;
        var mediaLibraries = await mediaLibraryService.GetAll();

        foreach (var library in mediaLibraries)
        {
            if (library.Paths == null) continue;

            foreach (var libraryPath in library.Paths)
            {
                var normalizedLibraryPath = libraryPath.StandardizePath();
                if (string.IsNullOrEmpty(normalizedLibraryPath)) continue;

                // Check if the path is under this library's path
                if (normalizedPath.StartsWith(normalizedLibraryPath, StringComparison.OrdinalIgnoreCase))
                {
                    return library.Id;
                }
            }
        }

        return null;
    }
}
