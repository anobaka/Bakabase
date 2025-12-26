using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReservedPropertyValue = Bakabase.Abstractions.Models.Domain.ReservedPropertyValue;

namespace Bakabase.InsideWorld.Business.Components.Search.Index;

/// <summary>
/// 资源搜索倒排索引服务实现
/// </summary>
public class ResourceSearchIndexService : IResourceSearchIndexService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ResourceSearchIndexService> _logger;
    private readonly ResourceSearchIndex _index = new();

    // Channel for async batch updates
    private readonly Channel<IndexOperation> _operationChannel =
        Channel.CreateUnbounded<IndexOperation>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    // Batch update configuration
    private const int BatchSize = 100;
    private const int MaxDelayMs = 500;
    private const int MinBatchIntervalMs = 50;

    // State management
    private volatile bool _isReady;
    private TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _backgroundCts = new();
    private Task? _backgroundTask;

    public bool IsReady => _isReady;
    public long Version => _index.Version;
    public DateTime LastUpdatedAt => _index.LastUpdatedAt;

    public ResourceSearchIndexService(
        IServiceScopeFactory scopeFactory,
        IResourceDataChangeEvent resourceDataChangeEvent,
        ILogger<ResourceSearchIndexService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        // Subscribe to resource data change events
        resourceDataChangeEvent.OnResourceDataChanged += OnResourceDataChanged;
        resourceDataChangeEvent.OnResourceRemoved += OnResourceRemoved;

        // Start background processing for incremental updates
        _backgroundTask = ProcessOperationsAsync(_backgroundCts.Token);
    }

    private void OnResourceDataChanged(ResourceDataChangedEventArgs args)
    {
        InvalidateResources(args.ResourceIds);
    }

    private void OnResourceRemoved(ResourceRemovedEventArgs args)
    {
        RemoveResources(args.ResourceIds);
    }

    #region Invalidation Methods

    public void InvalidateResource(int resourceId)
    {
        _operationChannel.Writer.TryWrite(new IndexOperation(IndexOperationType.Update, resourceId));
    }

    public void InvalidateResources(IEnumerable<int> resourceIds)
    {
        foreach (var resourceId in resourceIds)
        {
            _operationChannel.Writer.TryWrite(new IndexOperation(IndexOperationType.Update, resourceId));
        }
    }

    public void RemoveResource(int resourceId)
    {
        _operationChannel.Writer.TryWrite(new IndexOperation(IndexOperationType.Remove, resourceId));
    }

    public void RemoveResources(IEnumerable<int> resourceIds)
    {
        foreach (var resourceId in resourceIds)
        {
            _operationChannel.Writer.TryWrite(new IndexOperation(IndexOperationType.Remove, resourceId));
        }
    }

    #endregion

    #region Background Processing

    private async Task ProcessOperationsAsync(CancellationToken ct)
    {
        var batch = new List<IndexOperation>(BatchSize);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                batch.Clear();

                // Wait for the first operation
                if (await _operationChannel.Reader.WaitToReadAsync(ct))
                {
                    // Collect a batch of operations (max wait MaxDelayMs)
                    var deadline = DateTime.UtcNow.AddMilliseconds(MaxDelayMs);

                    while (batch.Count < BatchSize &&
                           DateTime.UtcNow < deadline &&
                           _operationChannel.Reader.TryRead(out var op))
                    {
                        batch.Add(op);
                    }

                    if (batch.Count > 0)
                    {
                        await ProcessBatchAsync(batch, ct);

                        // Brief pause between batches to avoid resource overuse
                        await Task.Delay(MinBatchIntervalMs, ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing index operations batch");
                await Task.Delay(1000, ct); // Wait before retrying
            }
        }
    }

    private async Task ProcessBatchAsync(List<IndexOperation> operations, CancellationToken ct)
    {
        // Group by operation type and deduplicate
        var toRemove = operations
            .Where(o => o.Type == IndexOperationType.Remove)
            .Select(o => o.ResourceId)
            .Distinct()
            .ToArray();

        var toUpdate = operations
            .Where(o => o.Type == IndexOperationType.Update)
            .Select(o => o.ResourceId)
            .Distinct()
            .Except(toRemove) // Don't update what we're removing
            .ToArray();

        // Process removals first
        foreach (var resourceId in toRemove)
        {
            RemoveResourceFromIndex(resourceId);
        }

        // Then process updates
        if (toUpdate.Length > 0)
        {
            await UpdateResourcesIndexAsync(toUpdate, ct);
        }

        if (toRemove.Length > 0 || toUpdate.Length > 0)
        {
            _index.Version++;
            _index.LastUpdatedAt = DateTime.UtcNow;
        }
    }

    #endregion

    #region Index Update Methods

    private async Task UpdateResourcesIndexAsync(int[] resourceIds, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        try
        {
            // Load property values for resources
            var customPropertyValueService = scope.ServiceProvider
                .GetRequiredService<ICustomPropertyValueService>();
            var reservedPropertyValueService = scope.ServiceProvider
                .GetRequiredService<IReservedPropertyValueService>();
            var resourceOrm = scope.ServiceProvider
                .GetRequiredService<IResourceService>();
            var mediaLibraryResourceMappingService = scope.ServiceProvider
                .GetRequiredService<IMediaLibraryResourceMappingService>();

            var customPropertyValues = await customPropertyValueService
                .GetAll(x => resourceIds.Contains(x.ResourceId),
                    InsideWorld.Models.Constants.AdditionalItems.CustomPropertyValueAdditionalItem.None, false);
            var customPropertyService = scope.ServiceProvider
                .GetRequiredService<ICustomPropertyService>();
            var customProperties = await customPropertyService.GetAll();
            var propertyMap = customProperties.ToDictionary(p => p.Id, p => p.ToProperty());

            var reservedPropertyValues = await reservedPropertyValueService
                .GetAll(x => resourceIds.Contains(x.ResourceId));
            var resourceDbModels = await resourceOrm.GetAllDbModels(x => resourceIds.Contains(x.Id));
            var dbResourceMap = resourceDbModels.ToDictionary(r => r.Id, r => r);
            var mediaLibraryMappings = await mediaLibraryResourceMappingService
                .GetMediaLibraryIdsByResourceIds(resourceIds);

            // Group property values by resource ID
            var customValuesByResource = customPropertyValues
                .GroupBy(v => v.ResourceId)
                .ToDictionary(g => g.Key, g => g.ToList());
            var reservedValuesByResource = reservedPropertyValues
                .GroupBy(v => v.ResourceId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var resourceId in resourceIds)
            {
                ct.ThrowIfCancellationRequested();

                // 1. Clear old index
                RemoveResourceFromIndex(resourceId);

                // 2. Build new index
                var indexKeys = new HashSet<IndexKey>();

                // Index internal properties
                var dbModel = dbResourceMap.GetValueOrDefault(resourceId);
                if (dbModel != null)
                {
                    IndexInternalProperties(resourceId, dbModel, mediaLibraryMappings, indexKeys);
                }

                // Index reserved properties
                if (reservedValuesByResource.TryGetValue(resourceId, out var reserved))
                {
                    foreach (var value in reserved)
                    {
                        IndexReservedProperty(resourceId, value, indexKeys);
                    }
                }

                // Index custom properties
                if (customValuesByResource.TryGetValue(resourceId, out var custom))
                {
                    foreach (var value in custom)
                    {
                        IndexCustomProperty(resourceId, value, propertyMap, indexKeys);
                    }
                }

                // 3. Save index key mapping
                _index.ResourceIndexKeys[resourceId] = indexKeys;
                lock (_index.AllResourceIdsLock)
                {
                    _index.AllResourceIds.Add(resourceId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating index for resources: {ResourceIds}",
                string.Join(",", resourceIds.Take(10)));
        }
    }

    private void RemoveResourceFromIndex(int resourceId)
    {
        if (!_index.ResourceIndexKeys.TryRemove(resourceId, out var indexKeys))
        {
            // Resource not in index
            lock (_index.AllResourceIdsLock)
            {
                _index.AllResourceIds.Remove(resourceId);
            }
            return;
        }

        // Remove from value index
        foreach (var key in indexKeys)
        {
            if (_index.ValueIndex.TryGetValue(key.Pool, out var poolIndex) &&
                poolIndex.TryGetValue(key.PropertyId, out var propIndex) &&
                propIndex.TryGetValue(key.ValueKey, out var resourceIds))
            {
                lock (resourceIds)
                {
                    resourceIds.Remove(resourceId);
                }
            }
        }

        // Remove from range index (by checking all pools/properties - less efficient but necessary)
        foreach (var (pool, poolIndex) in _index.RangeIndex)
        {
            foreach (var (propId, sortedList) in poolIndex)
            {
                lock (sortedList)
                {
                    foreach (var (_, resourceIds) in sortedList)
                    {
                        resourceIds.Remove(resourceId);
                    }
                }
            }
        }

        lock (_index.AllResourceIdsLock)
        {
            _index.AllResourceIds.Remove(resourceId);
        }
    }

    #endregion

    #region Index Building Methods

    private void IndexInternalProperties(
        int resourceId,
        ResourceDbModel dbModel,
        Dictionary<int, HashSet<int>>? mediaLibraryMappings,
        HashSet<IndexKey> indexKeys)
    {
        // Filename
        var filename = Path.GetFileName(dbModel.Path);
        if (!string.IsNullOrEmpty(filename))
        {
            AddToValueIndex(PropertyPool.Internal, (int)InternalProperty.Filename,
                filename, resourceId, indexKeys);
        }

        // Directory path
        var dirPath = Path.GetDirectoryName(dbModel.Path);
        if (!string.IsNullOrEmpty(dirPath))
        {
            AddToValueIndex(PropertyPool.Internal, (int)InternalProperty.DirectoryPath,
                dirPath, resourceId, indexKeys);
        }

        // Root path
        AddToValueIndex(PropertyPool.Internal, (int)InternalProperty.RootPath,
            dbModel.Path, resourceId, indexKeys);

        // Created at (range index)
        AddToRangeIndex(PropertyPool.Internal, (int)InternalProperty.CreatedAt,
            dbModel.CreateDt, resourceId, indexKeys);

        // File created at
        AddToRangeIndex(PropertyPool.Internal, (int)InternalProperty.FileCreatedAt,
            dbModel.FileCreateDt, resourceId, indexKeys);

        // File modified at
        AddToRangeIndex(PropertyPool.Internal, (int)InternalProperty.FileModifiedAt,
            dbModel.FileModifyDt, resourceId, indexKeys);

        // Played at
        if (dbModel.PlayedAt.HasValue)
        {
            AddToRangeIndex(PropertyPool.Internal, (int)InternalProperty.PlayedAt,
                dbModel.PlayedAt.Value, resourceId, indexKeys);
        }

        // Media library multi
        if (mediaLibraryMappings?.TryGetValue(resourceId, out var mlIds) == true && mlIds.Count > 0)
        {
            foreach (var mlId in mlIds)
            {
                AddToValueIndex(PropertyPool.Internal, (int)InternalProperty.MediaLibraryV2Multi,
                    mlId.ToString(), resourceId, indexKeys);
            }
        }
    }

    private void IndexReservedProperty(
        int resourceId,
        ReservedPropertyValue value,
        HashSet<IndexKey> indexKeys)
    {
        // Rating (range index)
        if (value.Rating.HasValue)
        {
            AddToRangeIndex(PropertyPool.Reserved, (int)Abstractions.Models.Domain.Constants.ReservedProperty.Rating,
                value.Rating.Value, resourceId, indexKeys);
        }

        // Introduction
        if (!string.IsNullOrEmpty(value.Introduction))
        {
            AddToValueIndex(PropertyPool.Reserved, (int)Abstractions.Models.Domain.Constants.ReservedProperty.Introduction,
                value.Introduction, resourceId, indexKeys);
        }

        // Cover paths
        if (value.CoverPaths?.Any() == true)
        {
            foreach (var coverPath in value.CoverPaths)
            {
                AddToValueIndex(PropertyPool.Reserved, (int)Abstractions.Models.Domain.Constants.ReservedProperty.Cover,
                    coverPath, resourceId, indexKeys);
            }
        }
    }

    private void IndexCustomProperty(
        int resourceId,
        CustomPropertyValue value,
        Dictionary<int, Bakabase.Abstractions.Models.Domain.Property> propertyMap,
        HashSet<IndexKey> indexKeys)
    {
        if (value.Value == null) return;

        // Try to get the property definition to use IPropertyIndexProvider
        if (propertyMap.TryGetValue(value.PropertyId, out var property))
        {
            var indexProvider = PropertySystem.Property.TryGetIndexProvider(property.Type);
            if (indexProvider != null)
            {
                foreach (var entry in indexProvider.GenerateIndexEntries(property, value.Value))
                {
                    // Add to value index
                    AddToValueIndex(PropertyPool.Custom, value.PropertyId, entry.Key, resourceId, indexKeys);

                    // Add to range index if RangeValue is provided
                    if (entry.RangeValue != null)
                    {
                        AddToRangeIndex(PropertyPool.Custom, value.PropertyId, entry.RangeValue, resourceId, indexKeys);
                    }
                }
                return;
            }
        }

        // Fallback: use legacy indexing for unknown property types
        var valueStr = ConvertToIndexableString(value.Value);
        if (valueStr != null)
        {
            AddToValueIndex(PropertyPool.Custom, value.PropertyId, valueStr, resourceId, indexKeys);
        }

        if (value.Value is IComparable comparable && IsNumericOrDateTime(value.Value))
        {
            AddToRangeIndex(PropertyPool.Custom, value.PropertyId, comparable, resourceId, indexKeys);
        }
    }

    private static string? ConvertToIndexableString(object? value)
    {
        if (value == null) return null;

        return value switch
        {
            string s => s,
            // For list types, generate individual keys (fallback behavior)
            IEnumerable<string> strings => string.Join("|", strings),
            IEnumerable<object> objects => string.Join("|", objects.Select(o => o?.ToString() ?? "")),
            _ => value.ToString()
        };
    }

    private static bool IsNumericOrDateTime(object value)
    {
        return value is int or long or float or double or decimal
            or DateTime or DateTimeOffset or TimeSpan;
    }

    private void AddToValueIndex(
        PropertyPool pool,
        int propertyId,
        string value,
        int resourceId,
        HashSet<IndexKey> indexKeys)
    {
        if (string.IsNullOrEmpty(value)) return;

        var normalizedValue = NormalizeValue(value);
        var key = new IndexKey(pool, propertyId, normalizedValue);

        var poolIndex = _index.ValueIndex.GetOrAdd(pool, _ => new());
        var propIndex = poolIndex.GetOrAdd(propertyId, _ => new());
        var resourceIds = propIndex.GetOrAdd(normalizedValue, _ => new HashSet<int>());

        lock (resourceIds)
        {
            resourceIds.Add(resourceId);
        }

        indexKeys.Add(key);
    }

    private void AddToRangeIndex(
        PropertyPool pool,
        int propertyId,
        IComparable value,
        int resourceId,
        HashSet<IndexKey> indexKeys)
    {
        var poolIndex = _index.RangeIndex.GetOrAdd(pool, _ => new());
        var sortedList = poolIndex.GetOrAdd(propertyId, _ => new SortedList<IComparable, HashSet<int>>());

        lock (sortedList)
        {
            if (!sortedList.TryGetValue(value, out var resourceIds))
            {
                resourceIds = new HashSet<int>();
                sortedList[value] = resourceIds;
            }
            resourceIds.Add(resourceId);
        }

        // Also create index key for range values (for cleanup)
        var key = new IndexKey(pool, propertyId, $"range:{value}");
        indexKeys.Add(key);
    }

    private static string NormalizeValue(string value)
    {
        // Trim and lowercase for case-insensitive matching
        var normalized = value.Trim().ToLowerInvariant();

        // Use string interning for common values to save memory
        return string.IsInterned(normalized) ?? string.Intern(normalized);
    }

    #endregion

    #region Full Rebuild

    public Task RebuildAllAsync(CancellationToken ct = default)
    {
        return RebuildAllAsync(null, ct);
    }

    /// <summary>
    /// 全量重建索引，支持进度回调（用于 BTask 集成）
    /// </summary>
    /// <param name="progressCallback">进度回调：(percentage, message) => Task</param>
    /// <param name="ct">取消令牌</param>
    public async Task RebuildAllAsync(Func<int, string?, Task>? progressCallback, CancellationToken ct = default)
    {
        _isReady = false;
        _readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var sw = Stopwatch.StartNew();
            using var scope = _scopeFactory.CreateScope();

            // Clear existing index
            _index.Clear();

            await ReportProgress(progressCallback, 0, "Loading resources...");

            // Load all data
            var resourceOrm = scope.ServiceProvider
                .GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>>();
            var customPropertyValueService = scope.ServiceProvider
                .GetRequiredService<ICustomPropertyValueService>();
            var customPropertyService = scope.ServiceProvider
                .GetRequiredService<ICustomPropertyService>();
            var reservedPropertyValueService = scope.ServiceProvider
                .GetRequiredService<IReservedPropertyValueService>();
            var mediaLibraryResourceMappingService = scope.ServiceProvider
                .GetRequiredService<IMediaLibraryResourceMappingService>();

            var allResources = await resourceOrm.GetAll(null, false);
            _logger.LogInformation("Loaded {Count} resources in {Ms}ms", allResources.Count, sw.ElapsedMilliseconds);

            await ReportProgress(progressCallback, 5, $"Loaded {allResources.Count} resources");

            sw.Restart();
            var customPropertyValues = await customPropertyValueService.GetAll(null,
                InsideWorld.Models.Constants.AdditionalItems.CustomPropertyValueAdditionalItem.None, false);
            _logger.LogInformation("Loaded {Count} custom property values in {Ms}ms",
                customPropertyValues.Count, sw.ElapsedMilliseconds);

            // Load custom properties for IPropertyIndexProvider lookup
            var customProperties = await customPropertyService.GetAll();
            var propertyMap = customProperties.ToDictionary(p => p.Id, p => p.ToProperty());
            _logger.LogInformation("Loaded {Count} custom properties", customProperties.Count);

            await ReportProgress(progressCallback, 10, $"Loaded {customPropertyValues.Count} custom property values");

            sw.Restart();
            var reservedPropertyValues = await reservedPropertyValueService.GetAll();
            _logger.LogInformation("Loaded {Count} reserved property values in {Ms}ms",
                reservedPropertyValues.Count, sw.ElapsedMilliseconds);

            await ReportProgress(progressCallback, 15, $"Loaded {reservedPropertyValues.Count} reserved property values");

            sw.Restart();
            var allResourceIds = allResources.Select(r => r.Id).ToArray();
            var mediaLibraryMappings = await mediaLibraryResourceMappingService
                .GetMediaLibraryIdsByResourceIds(allResourceIds);
            _logger.LogInformation("Loaded media library mappings in {Ms}ms", sw.ElapsedMilliseconds);

            await ReportProgress(progressCallback, 20, "Building index...");

            // Group property values by resource ID
            var customValuesByResource = customPropertyValues
                .GroupBy(v => v.ResourceId)
                .ToDictionary(g => g.Key, g => g.ToList());
            var reservedValuesByResource = reservedPropertyValues
                .GroupBy(v => v.ResourceId)
                .ToDictionary(g => g.Key, g => g.ToList());

            sw.Restart();
            var indexedCount = 0;
            var totalCount = allResources.Count;
            var lastReportedPercentage = 20;

            // Index all resources
            foreach (var resource in allResources)
            {
                ct.ThrowIfCancellationRequested();

                var indexKeys = new HashSet<IndexKey>();

                IndexInternalProperties(resource.Id, resource, mediaLibraryMappings, indexKeys);

                if (reservedValuesByResource.TryGetValue(resource.Id, out var reserved))
                {
                    foreach (var value in reserved)
                    {
                        IndexReservedProperty(resource.Id, value, indexKeys);
                    }
                }

                if (customValuesByResource.TryGetValue(resource.Id, out var custom))
                {
                    foreach (var value in custom)
                    {
                        IndexCustomProperty(resource.Id, value, propertyMap, indexKeys);
                    }
                }

                _index.ResourceIndexKeys[resource.Id] = indexKeys;
                _index.AllResourceIds.Add(resource.Id);
                indexedCount++;

                // Report progress every 5%
                if (totalCount > 0)
                {
                    var currentPercentage = 20 + (int)(indexedCount * 80.0 / totalCount);
                    if (currentPercentage >= lastReportedPercentage + 5)
                    {
                        lastReportedPercentage = currentPercentage;
                        await ReportProgress(progressCallback, currentPercentage, $"Indexed {indexedCount}/{totalCount} resources");
                    }
                }
            }

            _logger.LogInformation("Indexed {Count} resources in {Ms}ms", indexedCount, sw.ElapsedMilliseconds);

            _index.Version++;
            _index.LastUpdatedAt = DateTime.UtcNow;
            _isReady = true;
            _readyTcs.TrySetResult();

            await ReportProgress(progressCallback, 100, $"Completed: {indexedCount} resources indexed");

            _logger.LogInformation(
                "Index rebuild complete: {ResourceCount} resources, {ValueEntries} value entries, {RangeEntries} range entries",
                _index.AllResourceIds.Count,
                _index.GetValueIndexEntryCount(),
                _index.GetRangeIndexEntryCount());
        }
        catch (Exception ex)
        {
            _readyTcs.TrySetException(new InvalidOperationException("Index rebuild failed", ex));
            throw;
        }
    }

    private static async Task ReportProgress(Func<int, string?, Task>? callback, int percentage, string? message)
    {
        if (callback != null)
        {
            await callback(percentage, message);
        }
    }

    public async Task WaitForReadyAsync(TimeSpan? timeout = null)
    {
        if (_isReady) return;

        var task = _readyTcs.Task;
        if (timeout.HasValue)
        {
            using var cts = new CancellationTokenSource(timeout.Value);
            try
            {
                await task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Timed out waiting for search index to be ready");
            }
        }
        else
        {
            await task;
        }
    }

    #endregion

    #region Search Implementation

    public async Task<HashSet<int>?> SearchResourceIdsAsync(ResourceSearchFilterGroup? group)
    {
        if (group == null || group.Disabled)
        {
            return null; // No filter, return null to indicate "all"
        }

        if (!IsReady)
        {
            // Wait up to 1 second for index to be ready
            try
            {
                await WaitForReadyAsync(TimeSpan.FromSeconds(1));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Search index not ready, falling back to full scan");
                return null; // Fallback to full search
            }
        }

        return EvaluateFilterGroup(group);
    }

    private HashSet<int>? EvaluateFilterGroup(ResourceSearchFilterGroup group)
    {
        if (group.Disabled) return null;

        var results = new List<HashSet<int>?>();

        // Evaluate filters
        if (group.Filters != null)
        {
            foreach (var filter in group.Filters.Where(f => !f.Disabled))
            {
                var result = EvaluateFilter(filter);
                results.Add(result);
            }
        }

        // Evaluate sub-groups
        if (group.Groups != null)
        {
            foreach (var subGroup in group.Groups.Where(g => !g.Disabled))
            {
                var result = EvaluateFilterGroup(subGroup);
                results.Add(result);
            }
        }

        if (results.Count == 0) return null;

        // Combine results based on combinator
        return group.Combinator switch
        {
            SearchCombinator.And => IntersectResults(results),
            SearchCombinator.Or => UnionResults(results),
            _ => null
        };
    }

    private HashSet<int>? EvaluateFilter(ResourceSearchFilter filter)
    {
        return filter.Operation switch
        {
            SearchOperation.Equals => EvaluateEquals(filter),
            SearchOperation.NotEquals => EvaluateNotEquals(filter),
            SearchOperation.Contains => EvaluateContains(filter),
            SearchOperation.NotContains => EvaluateNotContains(filter),
            SearchOperation.StartsWith => EvaluateStartsWith(filter),
            SearchOperation.NotStartsWith => EvaluateNotStartsWith(filter),
            SearchOperation.EndsWith => EvaluateEndsWith(filter),
            SearchOperation.NotEndsWith => EvaluateNotEndsWith(filter),
            SearchOperation.GreaterThan => EvaluateGreaterThan(filter),
            SearchOperation.LessThan => EvaluateLessThan(filter),
            SearchOperation.GreaterThanOrEquals => EvaluateGreaterThanOrEquals(filter),
            SearchOperation.LessThanOrEquals => EvaluateLessThanOrEquals(filter),
            SearchOperation.IsNull => EvaluateIsNull(filter),
            SearchOperation.IsNotNull => EvaluateIsNotNull(filter),
            SearchOperation.In => EvaluateIn(filter),
            SearchOperation.NotIn => EvaluateNotIn(filter),
            SearchOperation.Matches => EvaluateMatches(filter),
            SearchOperation.NotMatches => EvaluateNotMatches(filter),
            _ => null
        };
    }

    #region Search Operations

    private HashSet<int>? EvaluateEquals(ResourceSearchFilter filter)
    {
        var normalizedValue = NormalizeValue(filter.DbValue?.ToString() ?? "");
        if (string.IsNullOrEmpty(normalizedValue)) return new HashSet<int>();

        var poolIndex = _index.ValueIndex.GetValueOrDefault(filter.PropertyPool);
        var propIndex = poolIndex?.GetValueOrDefault(filter.PropertyId);
        var resourceIds = propIndex?.GetValueOrDefault(normalizedValue);

        return resourceIds != null ? new HashSet<int>(resourceIds) : new HashSet<int>();
    }

    private HashSet<int>? EvaluateNotEquals(ResourceSearchFilter filter)
    {
        var equalIds = EvaluateEquals(filter);
        if (equalIds == null) return null;

        HashSet<int> allIds;
        lock (_index.AllResourceIdsLock)
        {
            allIds = new HashSet<int>(_index.AllResourceIds);
        }
        allIds.ExceptWith(equalIds);
        return allIds;
    }

    private HashSet<int>? EvaluateContains(ResourceSearchFilter filter)
    {
        var searchValue = NormalizeValue(filter.DbValue?.ToString() ?? "");
        if (string.IsNullOrEmpty(searchValue)) return new HashSet<int>();

        var poolIndex = _index.ValueIndex.GetValueOrDefault(filter.PropertyPool);
        var propIndex = poolIndex?.GetValueOrDefault(filter.PropertyId);
        if (propIndex == null) return new HashSet<int>();

        var result = new HashSet<int>();

        // Iterate all values, find those containing the search term
        foreach (var (value, resourceIds) in propIndex)
        {
            if (value.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
            {
                result.UnionWith(resourceIds);
            }
        }

        return result;
    }

    private HashSet<int>? EvaluateNotContains(ResourceSearchFilter filter)
    {
        var containsIds = EvaluateContains(filter);
        if (containsIds == null) return null;

        HashSet<int> allIds;
        lock (_index.AllResourceIdsLock)
        {
            allIds = new HashSet<int>(_index.AllResourceIds);
        }
        allIds.ExceptWith(containsIds);
        return allIds;
    }

    private HashSet<int>? EvaluateStartsWith(ResourceSearchFilter filter)
    {
        var searchValue = NormalizeValue(filter.DbValue?.ToString() ?? "");
        if (string.IsNullOrEmpty(searchValue)) return new HashSet<int>();

        var poolIndex = _index.ValueIndex.GetValueOrDefault(filter.PropertyPool);
        var propIndex = poolIndex?.GetValueOrDefault(filter.PropertyId);
        if (propIndex == null) return new HashSet<int>();

        var result = new HashSet<int>();

        foreach (var (value, resourceIds) in propIndex)
        {
            if (value.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
            {
                result.UnionWith(resourceIds);
            }
        }

        return result;
    }

    private HashSet<int>? EvaluateNotStartsWith(ResourceSearchFilter filter)
    {
        var startsWithIds = EvaluateStartsWith(filter);
        if (startsWithIds == null) return null;

        HashSet<int> allIds;
        lock (_index.AllResourceIdsLock)
        {
            allIds = new HashSet<int>(_index.AllResourceIds);
        }
        allIds.ExceptWith(startsWithIds);
        return allIds;
    }

    private HashSet<int>? EvaluateEndsWith(ResourceSearchFilter filter)
    {
        var searchValue = NormalizeValue(filter.DbValue?.ToString() ?? "");
        if (string.IsNullOrEmpty(searchValue)) return new HashSet<int>();

        var poolIndex = _index.ValueIndex.GetValueOrDefault(filter.PropertyPool);
        var propIndex = poolIndex?.GetValueOrDefault(filter.PropertyId);
        if (propIndex == null) return new HashSet<int>();

        var result = new HashSet<int>();

        foreach (var (value, resourceIds) in propIndex)
        {
            if (value.EndsWith(searchValue, StringComparison.OrdinalIgnoreCase))
            {
                result.UnionWith(resourceIds);
            }
        }

        return result;
    }

    private HashSet<int>? EvaluateNotEndsWith(ResourceSearchFilter filter)
    {
        var endsWithIds = EvaluateEndsWith(filter);
        if (endsWithIds == null) return null;

        HashSet<int> allIds;
        lock (_index.AllResourceIdsLock)
        {
            allIds = new HashSet<int>(_index.AllResourceIds);
        }
        allIds.ExceptWith(endsWithIds);
        return allIds;
    }

    private HashSet<int>? EvaluateGreaterThan(ResourceSearchFilter filter)
    {
        if (filter.DbValue is not IComparable compareValue) return new HashSet<int>();

        var poolIndex = _index.RangeIndex.GetValueOrDefault(filter.PropertyPool);
        var sortedList = poolIndex?.GetValueOrDefault(filter.PropertyId);
        if (sortedList == null) return new HashSet<int>();

        var result = new HashSet<int>();

        lock (sortedList)
        {
            foreach (var (value, resourceIds) in sortedList)
            {
                try
                {
                    if (value.CompareTo(compareValue) > 0)
                    {
                        result.UnionWith(resourceIds);
                    }
                }
                catch
                {
                    // Type mismatch, skip
                }
            }
        }

        return result;
    }

    private HashSet<int>? EvaluateLessThan(ResourceSearchFilter filter)
    {
        if (filter.DbValue is not IComparable compareValue) return new HashSet<int>();

        var poolIndex = _index.RangeIndex.GetValueOrDefault(filter.PropertyPool);
        var sortedList = poolIndex?.GetValueOrDefault(filter.PropertyId);
        if (sortedList == null) return new HashSet<int>();

        var result = new HashSet<int>();

        lock (sortedList)
        {
            foreach (var (value, resourceIds) in sortedList)
            {
                try
                {
                    if (value.CompareTo(compareValue) < 0)
                    {
                        result.UnionWith(resourceIds);
                    }
                }
                catch
                {
                    // Type mismatch, skip
                }
            }
        }

        return result;
    }

    private HashSet<int>? EvaluateGreaterThanOrEquals(ResourceSearchFilter filter)
    {
        if (filter.DbValue is not IComparable compareValue) return new HashSet<int>();

        var poolIndex = _index.RangeIndex.GetValueOrDefault(filter.PropertyPool);
        var sortedList = poolIndex?.GetValueOrDefault(filter.PropertyId);
        if (sortedList == null) return new HashSet<int>();

        var result = new HashSet<int>();

        lock (sortedList)
        {
            foreach (var (value, resourceIds) in sortedList)
            {
                try
                {
                    if (value.CompareTo(compareValue) >= 0)
                    {
                        result.UnionWith(resourceIds);
                    }
                }
                catch
                {
                    // Type mismatch, skip
                }
            }
        }

        return result;
    }

    private HashSet<int>? EvaluateLessThanOrEquals(ResourceSearchFilter filter)
    {
        if (filter.DbValue is not IComparable compareValue) return new HashSet<int>();

        var poolIndex = _index.RangeIndex.GetValueOrDefault(filter.PropertyPool);
        var sortedList = poolIndex?.GetValueOrDefault(filter.PropertyId);
        if (sortedList == null) return new HashSet<int>();

        var result = new HashSet<int>();

        lock (sortedList)
        {
            foreach (var (value, resourceIds) in sortedList)
            {
                try
                {
                    if (value.CompareTo(compareValue) <= 0)
                    {
                        result.UnionWith(resourceIds);
                    }
                }
                catch
                {
                    // Type mismatch, skip
                }
            }
        }

        return result;
    }

    private HashSet<int>? EvaluateIsNull(ResourceSearchFilter filter)
    {
        // Get all resource IDs that have a value for this property
        var hasValueIds = GetResourceIdsWithValue(filter.PropertyPool, filter.PropertyId);

        // Return all resources minus those with values
        HashSet<int> allIds;
        lock (_index.AllResourceIdsLock)
        {
            allIds = new HashSet<int>(_index.AllResourceIds);
        }
        allIds.ExceptWith(hasValueIds);
        return allIds;
    }

    private HashSet<int>? EvaluateIsNotNull(ResourceSearchFilter filter)
    {
        return GetResourceIdsWithValue(filter.PropertyPool, filter.PropertyId);
    }

    private HashSet<int>? EvaluateIn(ResourceSearchFilter filter)
    {
        if (filter.DbValue is not IEnumerable<object> values) return new HashSet<int>();

        var result = new HashSet<int>();
        foreach (var value in values)
        {
            var normalizedValue = NormalizeValue(value?.ToString() ?? "");
            if (string.IsNullOrEmpty(normalizedValue)) continue;

            var poolIndex = _index.ValueIndex.GetValueOrDefault(filter.PropertyPool);
            var propIndex = poolIndex?.GetValueOrDefault(filter.PropertyId);
            var resourceIds = propIndex?.GetValueOrDefault(normalizedValue);

            if (resourceIds != null)
            {
                result.UnionWith(resourceIds);
            }
        }

        return result;
    }

    private HashSet<int>? EvaluateNotIn(ResourceSearchFilter filter)
    {
        var inIds = EvaluateIn(filter);
        if (inIds == null) return null;

        HashSet<int> allIds;
        lock (_index.AllResourceIdsLock)
        {
            allIds = new HashSet<int>(_index.AllResourceIds);
        }
        allIds.ExceptWith(inIds);
        return allIds;
    }

    private HashSet<int>? EvaluateMatches(ResourceSearchFilter filter)
    {
        var pattern = filter.DbValue?.ToString();
        if (string.IsNullOrEmpty(pattern)) return new HashSet<int>();

        try
        {
            var regex = new System.Text.RegularExpressions.Regex(pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.Compiled,
                TimeSpan.FromSeconds(1));

            var poolIndex = _index.ValueIndex.GetValueOrDefault(filter.PropertyPool);
            var propIndex = poolIndex?.GetValueOrDefault(filter.PropertyId);
            if (propIndex == null) return new HashSet<int>();

            var result = new HashSet<int>();

            foreach (var (value, resourceIds) in propIndex)
            {
                if (regex.IsMatch(value))
                {
                    result.UnionWith(resourceIds);
                }
            }

            return result;
        }
        catch
        {
            // Invalid regex pattern
            return new HashSet<int>();
        }
    }

    private HashSet<int>? EvaluateNotMatches(ResourceSearchFilter filter)
    {
        var matchesIds = EvaluateMatches(filter);
        if (matchesIds == null) return null;

        HashSet<int> allIds;
        lock (_index.AllResourceIdsLock)
        {
            allIds = new HashSet<int>(_index.AllResourceIds);
        }
        allIds.ExceptWith(matchesIds);
        return allIds;
    }

    private HashSet<int> GetResourceIdsWithValue(PropertyPool pool, int propertyId)
    {
        var hasValueIds = new HashSet<int>();

        // Check value index
        var poolIndex = _index.ValueIndex.GetValueOrDefault(pool);
        var propIndex = poolIndex?.GetValueOrDefault(propertyId);

        if (propIndex != null)
        {
            foreach (var resourceIds in propIndex.Values)
            {
                hasValueIds.UnionWith(resourceIds);
            }
        }

        // Also check range index
        var rangePoolIndex = _index.RangeIndex.GetValueOrDefault(pool);
        var rangePropIndex = rangePoolIndex?.GetValueOrDefault(propertyId);

        if (rangePropIndex != null)
        {
            lock (rangePropIndex)
            {
                foreach (var resourceIds in rangePropIndex.Values)
                {
                    hasValueIds.UnionWith(resourceIds);
                }
            }
        }

        return hasValueIds;
    }

    #endregion

    #region Result Combinators

    private static HashSet<int>? IntersectResults(List<HashSet<int>?> results)
    {
        // Filter out nulls (meaning "all matches")
        var nonNullResults = results.Where(r => r != null).ToList();

        if (nonNullResults.Count == 0) return null; // All were null, return null (all)

        // Start from smallest set for optimization
        var sorted = nonNullResults.OrderBy(r => r!.Count).ToList();
        var result = new HashSet<int>(sorted[0]!);

        for (var i = 1; i < sorted.Count && result.Count > 0; i++)
        {
            result.IntersectWith(sorted[i]!);
        }

        return result;
    }

    private static HashSet<int>? UnionResults(List<HashSet<int>?> results)
    {
        // If any result is null (all matches), return null
        if (results.Any(r => r == null)) return null;

        var result = new HashSet<int>();
        foreach (var r in results)
        {
            result.UnionWith(r!);
        }
        return result;
    }

    #endregion

    #endregion

    #region Status

    public ResourceSearchIndexStatus GetStatus()
    {
        return new ResourceSearchIndexStatus
        {
            IsReady = _isReady,
            Version = _index.Version,
            LastUpdatedAt = _index.LastUpdatedAt,
            TotalResourceCount = _index.AllResourceIds.Count,
            PendingUpdateCount = _operationChannel.Reader.Count,
            IndexSizes = new Dictionary<string, int>
            {
                ["ValueIndex"] = _index.GetValueIndexEntryCount(),
                ["RangeIndex"] = _index.GetRangeIndexEntryCount()
            }
        };
    }

    #endregion
}
