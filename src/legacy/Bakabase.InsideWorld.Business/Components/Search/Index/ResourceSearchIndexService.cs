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
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Extensions;
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
    private readonly IBakabaseLocalizer _localizer;
    private readonly ResourceSearchIndex _index = new();

    // Channel for async batch updates
    private readonly Channel<IndexQueueItem> _operationChannel =
        Channel.CreateUnbounded<IndexQueueItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    // Batch update configuration. Bulk callers enqueue bounded chunks and the single
    // reader adaptively coalesces adjacent chunks up to this many distinct resources.
    // This keeps memory predictable while avoiding a full cache scan for every 100 IDs.
    private const int MaxBatchResourceCount = 4096;
    private const int MaxDelayMs = 500;
    private const int MinBatchIntervalMs = 50;
    private const int MaxBatchAttempts = 3;

    // State management
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly object _stateLock = new();
    private volatile bool _isReady;
    private TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _unrecoveredIndexFailure;
    private readonly CancellationTokenSource _backgroundCts = new();
    private Task? _backgroundTask;
    private long _pendingOperationCount;

    private abstract record IndexQueueItem;

    private sealed record OperationBatchQueueItem(IReadOnlyList<IndexOperation> Operations) : IndexQueueItem;

    private sealed record BarrierQueueItem(TaskCompletionSource Completion) : IndexQueueItem;

    public bool IsReady => _isReady;
    public long Version => _index.Version;
    public DateTime LastUpdatedAt => _index.LastUpdatedAt;

    public ResourceSearchIndexService(
        IServiceScopeFactory scopeFactory,
        IResourceDataChangeEvent resourceDataChangeEvent,
        ILogger<ResourceSearchIndexService> logger,
        IBakabaseLocalizer localizer)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _localizer = localizer;

        // Subscribe to resource data change events
        resourceDataChangeEvent.OnResourceDataChanged += OnResourceDataChanged;
        resourceDataChangeEvent.OnResourceRemoved += OnResourceRemoved;

        // Start background processing for incremental updates
        _backgroundTask = ProcessOperationsAsync(_backgroundCts.Token);
    }

    private static TaskCompletionSource CreateReadyCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Exception? GetUnrecoveredIndexFailure()
    {
        lock (_stateLock)
        {
            return _unrecoveredIndexFailure;
        }
    }

    private void MarkIndexUnavailable(Exception failure)
    {
        TaskCompletionSource readyTcs;
        lock (_stateLock)
        {
            _unrecoveredIndexFailure ??= failure;
            _isReady = false;
            if (_readyTcs.Task.IsCompleted)
            {
                _readyTcs = CreateReadyCompletionSource();
            }
            readyTcs = _readyTcs;
        }

        readyTcs.TrySetException(new InvalidOperationException("Resource search index update failed", failure));
    }

    private void BeginRebuild()
    {
        lock (_stateLock)
        {
            _isReady = false;
            if (_readyTcs.Task.IsCompleted)
            {
                _readyTcs = CreateReadyCompletionSource();
            }
        }
    }

    private void CompleteRebuild()
    {
        TaskCompletionSource readyTcs;
        lock (_stateLock)
        {
            _unrecoveredIndexFailure = null;
            _isReady = true;
            readyTcs = _readyTcs;
        }

        readyTcs.TrySetResult();
    }

    private void FailRebuild(Exception failure)
    {
        TaskCompletionSource readyTcs;
        lock (_stateLock)
        {
            _unrecoveredIndexFailure = failure;
            _isReady = false;
            if (_readyTcs.Task.IsCompleted)
            {
                _readyTcs = CreateReadyCompletionSource();
            }
            readyTcs = _readyTcs;
        }

        readyTcs.TrySetException(new InvalidOperationException("Index rebuild failed", failure));
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
        EnqueueOperations(IndexOperationType.Update, [resourceId]);
    }

    public void InvalidateResources(IEnumerable<int> resourceIds)
    {
        EnqueueOperations(IndexOperationType.Update, resourceIds);
    }

    public void RemoveResource(int resourceId)
    {
        EnqueueOperations(IndexOperationType.Remove, [resourceId]);
    }

    public void RemoveResources(IEnumerable<int> resourceIds)
    {
        EnqueueOperations(IndexOperationType.Remove, resourceIds);
    }

    public async Task WaitForPendingUpdatesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_operationChannel.Writer.TryWrite(new BarrierQueueItem(completion)))
        {
            throw new InvalidOperationException("The resource search index update queue is not accepting work");
        }

        using var registration = ct.Register(() => completion.TrySetCanceled(ct));
        await completion.Task;
    }

    private void EnqueueOperations(IndexOperationType type, IEnumerable<int> resourceIds)
    {
        // Deduplicate one bulk notification before it reaches the channel. Very large
        // notifications are split so no individual queue item or processing batch is
        // unbounded; the reader can still merge adjacent chunks when IDs overlap.
        foreach (var chunk in resourceIds.Distinct().Chunk(MaxBatchResourceCount))
        {
            var operations = chunk.Select(resourceId => new IndexOperation(type, resourceId)).ToArray();
            Interlocked.Add(ref _pendingOperationCount, operations.Length);

            if (_operationChannel.Writer.TryWrite(new OperationBatchQueueItem(operations)))
            {
                continue;
            }

            Interlocked.Add(ref _pendingOperationCount, -operations.Length);
            _logger.LogError(
                "Failed to enqueue {OperationType} operations for {ResourceCount} resources",
                type,
                operations.Length);
            MarkIndexUnavailable(new InvalidOperationException(
                $"Failed to enqueue {type} operations for {operations.Length} resources"));
            return;
        }
    }

    #endregion

    #region Background Processing

    private async Task ProcessOperationsAsync(CancellationToken ct)
    {
        var batch = new Dictionary<int, IndexOperationType>(MaxBatchResourceCount);
        IndexQueueItem? deferredItem = null;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                IndexQueueItem? item;
                if (deferredItem != null)
                {
                    item = deferredItem;
                    deferredItem = null;
                }
                else
                {
                    // Wait for the first operation or barrier.
                    if (!await _operationChannel.Reader.WaitToReadAsync(ct))
                    {
                        break;
                    }

                    if (!_operationChannel.Reader.TryRead(out item))
                    {
                        continue;
                    }
                }

                batch.Clear();
                var deadline = DateTime.UtcNow.AddMilliseconds(MaxDelayMs);
                BarrierQueueItem? barrier = null;
                var channelCompleted = false;

                while (item != null)
                {
                    if (item is BarrierQueueItem barrierItem)
                    {
                        // Never read or merge work beyond a barrier. Its completion therefore
                        // remains an exact FIFO acknowledgement of the preceding prefix.
                        barrier = barrierItem;
                        break;
                    }

                    var operationItem = (OperationBatchQueueItem)item;
                    var consumed = MergeOperations(batch, operationItem.Operations);
                    Interlocked.Add(ref _pendingOperationCount, -consumed);

                    if (consumed < operationItem.Operations.Count)
                    {
                        // This queue item exceeded the bounded number of distinct resources.
                        // Keep its unconsumed suffix ahead of every item still in the channel.
                        deferredItem = new OperationBatchQueueItem(operationItem.Operations
                            .Skip(consumed)
                            .ToArray());
                        break;
                    }

                    if (batch.Count >= MaxBatchResourceCount)
                    {
                        break;
                    }

                    if (_operationChannel.Reader.TryRead(out item))
                    {
                        continue;
                    }

                    // Give adjacent small notifications a short bounded window to arrive.
                    // A barrier arriving in this window is consumed on the next iteration and
                    // ends the batch immediately.
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    using var collectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    collectionCts.CancelAfter(remaining);
                    try
                    {
                        if (!await _operationChannel.Reader.WaitToReadAsync(collectionCts.Token))
                        {
                            channelCompleted = true;
                            break;
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        break;
                    }

                    _operationChannel.Reader.TryRead(out item);
                }

                if (batch.Count > 0)
                {
                    await ProcessBatchWithRetryAsync(batch, ct);
                }

                if (barrier != null)
                {
                    await CompleteBarrierAsync(barrier, ct);
                }

                if (batch.Count > 0)
                {
                    // Brief pause between batches to avoid resource overuse.
                    await Task.Delay(MinBatchIntervalMs, ct);
                }

                if (channelCompleted && deferredItem == null)
                {
                    break;
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

    private static int MergeOperations(
        Dictionary<int, IndexOperationType> batch,
        IReadOnlyList<IndexOperation> operations)
    {
        var consumed = 0;
        foreach (var operation in operations)
        {
            if (!batch.TryGetValue(operation.ResourceId, out var existingType))
            {
                if (batch.Count >= MaxBatchResourceCount)
                {
                    break;
                }

                batch[operation.ResourceId] = operation.Type;
            }
            else if (existingType == IndexOperationType.Remove || operation.Type == IndexOperationType.Remove)
            {
                // A removal wins within one barrier-delimited batch. Re-reading a resource
                // that was removed in the same logical change can resurrect stale entries.
                batch[operation.ResourceId] = IndexOperationType.Remove;
            }

            consumed++;
        }

        return consumed;
    }

    private async Task ProcessBatchWithRetryAsync(
        IReadOnlyDictionary<int, IndexOperationType> operations,
        CancellationToken ct)
    {
        Exception? lastError = null;

        await _mutationGate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= MaxBatchAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await ProcessBatchAsync(operations, ct);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _logger.LogWarning(ex,
                        "Resource search index batch failed on attempt {Attempt}/{MaxAttempts}",
                        attempt,
                        MaxBatchAttempts);
                }

                if (attempt < MaxBatchAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), ct);
                }
            }

            var failure = new InvalidOperationException(
                $"Resource search index batch failed after {MaxBatchAttempts} attempts",
                lastError);
            MarkIndexUnavailable(failure);
            _logger.LogError(failure, "Resource search index is unavailable until a full rebuild succeeds");
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task CompleteBarrierAsync(BarrierQueueItem barrier, CancellationToken ct)
    {
        try
        {
            // Even an empty barrier must pass through the writer gate so it cannot complete
            // while a full rebuild is still mutating the index.
            await _mutationGate.WaitAsync(ct);
            try
            {
                var failure = GetUnrecoveredIndexFailure();
                if (failure == null)
                {
                    barrier.Completion.TrySetResult();
                }
                else
                {
                    barrier.Completion.TrySetException(new InvalidOperationException(
                        "A resource search index update before this barrier failed",
                        failure));
                }
            }
            finally
            {
                _mutationGate.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            barrier.Completion.TrySetCanceled(ct);
            throw;
        }
    }

    private async Task ProcessBatchAsync(
        IReadOnlyDictionary<int, IndexOperationType> operations,
        CancellationToken ct)
    {
        // Operations have already been deduplicated across adjacent queue items.
        var toRemove = operations
            .Where(o => o.Value == IndexOperationType.Remove)
            .Select(o => o.Key)
            .ToArray();

        var toUpdate = operations
            .Where(o => o.Value == IndexOperationType.Update)
            .Select(o => o.Key)
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
        var resourceIdSet = resourceIds.ToHashSet();

        try
        {
            // Load property values for resources
            var customPropertyValueService = scope.ServiceProvider
                .GetRequiredService<ICustomPropertyValueService>();
            var reservedPropertyValueService = scope.ServiceProvider
                .GetRequiredService<IReservedPropertyValueService>();
            var resourceOrm = scope.ServiceProvider
                .GetRequiredService<IResourceService>();
            // Optional: HealthScore reader is registered only when the HealthScore module is wired up.
            var healthScoreReader = scope.ServiceProvider
                .GetService<IResourceHealthScoreReader>();
            var mediaLibraryResourceMappingService = scope.ServiceProvider
                .GetRequiredService<IMediaLibraryResourceMappingService>();

            var customPropertyValues = await customPropertyValueService
                .GetAll(x => resourceIdSet.Contains(x.ResourceId),
                    InsideWorld.Models.Constants.AdditionalItems.CustomPropertyValueAdditionalItem.None, false);
            var customPropertyService = scope.ServiceProvider
                .GetRequiredService<ICustomPropertyService>();
            var customProperties = await customPropertyService.GetAll();
            var propertyMap = customProperties.ToDictionary(p => p.Id, p => p.ToProperty());

            var reservedPropertyValues = await reservedPropertyValueService
                .GetAll(x => resourceIdSet.Contains(x.ResourceId));
            var resourceDbModels = await resourceOrm.GetAllDbModels(x => resourceIdSet.Contains(x.Id));
            var dbResourceMap = resourceDbModels.ToDictionary(r => r.Id, r => r);
            var mediaLibraryMappings = await mediaLibraryResourceMappingService
                .GetMediaLibraryIdsByResourceIds(resourceIds);
            var collectionMappings = await LoadCollectionMappings(scope.ServiceProvider, resourceIds);

            var sourceLinkService = scope.ServiceProvider
                .GetRequiredService<IResourceSourceLinkService>();
            var sourceLinks = await sourceLinkService.GetByResourceIds(resourceIds);
            var sourceLinkMappings = sourceLinks
                .GroupBy(l => l.ResourceId)
                .ToDictionary(g => g.Key, g => g.Select(l => l.Source).ToHashSet());

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
                // Register the live key set before adding entries so a retry can remove
                // everything written by a partially completed attempt.
                _index.ResourceIndexKeys[resourceId] = indexKeys;

                // Index internal properties
                var dbModel = dbResourceMap.GetValueOrDefault(resourceId);
                if (dbModel != null)
                {
                    IndexInternalProperties(resourceId, dbModel, mediaLibraryMappings, sourceLinkMappings,
                        collectionMappings, indexKeys);
                }

                // Index aggregated health score (sourced from the HealthScore module's in-memory cache).
                var hs = healthScoreReader?.GetAggregatedScore(resourceId);
                if (hs.HasValue)
                {
                    AddToValueIndex(PropertyPool.Internal, (int)InternalProperty.HealthScore,
                        hs.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        resourceId, indexKeys);
                    AddToRangeIndex(PropertyPool.Internal, (int)InternalProperty.HealthScore,
                        hs.Value, resourceId, indexKeys);
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

                // 3. Mark the resource as fully indexed
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
            throw;
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

    /// <summary>
    /// Which collections each resource belongs to. Optional by design: a build without the
    /// collection module indexes everything else exactly as before.
    /// </summary>
    private static async Task<Dictionary<int, List<int>>?> LoadCollectionMappings(
        IServiceProvider services, int[] resourceIds)
    {
        var provider = services.GetService<ICollectionNameProvider>();

        if (provider == null || resourceIds.Length == 0) return null;

        var byResource = await provider.GetByResourceIdsAsync(resourceIds);

        return byResource.ToDictionary(kv => kv.Key, kv => kv.Value.Select(c => c.Id).ToList());
    }

    #region Index Building Methods

    private void IndexInternalProperties(
        int resourceId,
        ResourceDbModel dbModel,
        Dictionary<int, HashSet<int>>? mediaLibraryMappings,
        Dictionary<int, HashSet<ResourceSource>>? sourceLinkMappings,
        Dictionary<int, List<int>>? collectionMappings,
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

        // Has local path: derived from Path, always indexed so both true and false are searchable.
        AddToValueIndex(PropertyPool.Internal, (int)InternalProperty.HasLocalPath,
            (!string.IsNullOrEmpty(dbModel.Path)).ToString(), resourceId, indexKeys);

        // Created at (range index)
        AddToRangeIndex(PropertyPool.Internal, (int)InternalProperty.CreatedAt,
            dbModel.CreateDt, resourceId, indexKeys);

        // File times, but only for a resource that has files. Without a path these columns hold
        // the moment the row was written, which is not a file time at all — indexing it would let
        // "modified before X" match a work nobody has downloaded.
        if (!string.IsNullOrEmpty(dbModel.Path))
        {
            // File created at
            AddToRangeIndex(PropertyPool.Internal, (int)InternalProperty.FileCreatedAt,
                dbModel.FileCreateDt, resourceId, indexKeys);

            // File modified at
            AddToRangeIndex(PropertyPool.Internal, (int)InternalProperty.FileModifiedAt,
                dbModel.FileModifyDt, resourceId, indexKeys);
        }

        // Played at
        if (dbModel.PlayedAt.HasValue)
        {
            AddToRangeIndex(PropertyPool.Internal, (int)InternalProperty.PlayedAt,
                dbModel.PlayedAt.Value, resourceId, indexKeys);
        }

        // Parent resource
        if (dbModel.ParentId.HasValue)
        {
            AddToValueIndex(PropertyPool.Internal, (int)InternalProperty.ParentResource,
                dbModel.ParentId.Value.ToString(), resourceId, indexKeys);
        }

        // Collections. Rule-matched memberships are in here too — the provider returns both — so
        // "everything in this series" answers the same whether the series is a list or a rule.
        if (collectionMappings?.TryGetValue(resourceId, out var collectionIds) == true &&
            collectionIds.Count > 0)
        {
            foreach (var collectionId in collectionIds)
            {
                AddToValueIndex(PropertyPool.Internal, (int) InternalProperty.CollectionMulti,
                    collectionId.ToString(), resourceId, indexKeys);
            }
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

        // Source links
        if (sourceLinkMappings?.TryGetValue(resourceId, out var sources) == true && sources.Count > 0)
        {
            foreach (var source in sources)
            {
                AddToValueIndex(PropertyPool.Internal, (int)InternalProperty.Source,
                    ((int)source).ToString(), resourceId, indexKeys);
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

        // Name
        if (!string.IsNullOrEmpty(value.Name))
        {
            AddToValueIndex(PropertyPool.Reserved, (int)Abstractions.Models.Domain.Constants.ReservedProperty.Name,
                value.Name, resourceId, indexKeys);
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
        await _mutationGate.WaitAsync(ct);
        try
        {
            BeginRebuild();
            var sw = Stopwatch.StartNew();
            using var scope = _scopeFactory.CreateScope();

            // Clear existing index
            _index.Clear();

            await ReportProgress(progressCallback, 0, _localizer.SearchIndex_LoadingResources());

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
            var sourceLinkService = scope.ServiceProvider
                .GetRequiredService<IResourceSourceLinkService>();

            var allResources = await resourceOrm.GetAll(null, false);
            _logger.LogInformation("Loaded {Count} resources in {Ms}ms", allResources.Count, sw.ElapsedMilliseconds);

            await ReportProgress(progressCallback, 5, _localizer.SearchIndex_LoadedResources(allResources.Count));

            sw.Restart();
            var customPropertyValues = await customPropertyValueService.GetAll(null,
                InsideWorld.Models.Constants.AdditionalItems.CustomPropertyValueAdditionalItem.None, false);
            _logger.LogInformation("Loaded {Count} custom property values in {Ms}ms",
                customPropertyValues.Count, sw.ElapsedMilliseconds);

            // Load custom properties for IPropertyIndexProvider lookup
            var customProperties = await customPropertyService.GetAll();
            var propertyMap = customProperties.ToDictionary(p => p.Id, p => p.ToProperty());
            _logger.LogInformation("Loaded {Count} custom properties", customProperties.Count);

            await ReportProgress(progressCallback, 10, _localizer.SearchIndex_LoadedCustomPropertyValues(customPropertyValues.Count));

            sw.Restart();
            var reservedPropertyValues = await reservedPropertyValueService.GetAll();
            _logger.LogInformation("Loaded {Count} reserved property values in {Ms}ms",
                reservedPropertyValues.Count, sw.ElapsedMilliseconds);

            await ReportProgress(progressCallback, 15, _localizer.SearchIndex_LoadedReservedPropertyValues(reservedPropertyValues.Count));

            sw.Restart();
            var allResourceIds = allResources.Select(r => r.Id).ToArray();
            var mediaLibraryMappings = await mediaLibraryResourceMappingService
                .GetMediaLibraryIdsByResourceIds(allResourceIds);
            var collectionMappings = await LoadCollectionMappings(scope.ServiceProvider, allResourceIds);
            _logger.LogInformation("Loaded media library mappings in {Ms}ms", sw.ElapsedMilliseconds);

            sw.Restart();
            var allSourceLinks = await sourceLinkService.GetAll();
            var sourceLinkMappings = allSourceLinks
                .GroupBy(l => l.ResourceId)
                .ToDictionary(g => g.Key, g => g.Select(l => l.Source).ToHashSet());
            _logger.LogInformation("Loaded {Count} source links in {Ms}ms", allSourceLinks.Count, sw.ElapsedMilliseconds);

            await ReportProgress(progressCallback, 20, _localizer.SearchIndex_BuildingIndex());

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

                IndexInternalProperties(resource.Id, resource, mediaLibraryMappings, sourceLinkMappings,
                    collectionMappings, indexKeys);

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
                lock (_index.AllResourceIdsLock)
                {
                    _index.AllResourceIds.Add(resource.Id);
                }
                indexedCount++;

                // Report progress every 5%
                if (totalCount > 0)
                {
                    var currentPercentage = 20 + (int)(indexedCount * 80.0 / totalCount);
                    if (currentPercentage >= lastReportedPercentage + 5)
                    {
                        lastReportedPercentage = currentPercentage;
                        await ReportProgress(progressCallback, currentPercentage, _localizer.SearchIndex_IndexingProgress(indexedCount, totalCount));
                    }
                }
            }

            _logger.LogInformation("Indexed {Count} resources in {Ms}ms", indexedCount, sw.ElapsedMilliseconds);

            await ReportProgress(progressCallback, 100, _localizer.SearchIndex_Completed(indexedCount));

            _logger.LogInformation(
                "Index rebuild complete: {ResourceCount} resources, {ValueEntries} value entries, {RangeEntries} range entries",
                _index.AllResourceIds.Count,
                _index.GetValueIndexEntryCount(),
                _index.GetRangeIndexEntryCount());

            _index.Version++;
            _index.LastUpdatedAt = DateTime.UtcNow;
            CompleteRebuild();
        }
        catch (Exception ex)
        {
            FailRebuild(ex);
            throw;
        }
        finally
        {
            _mutationGate.Release();
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
        Task task;
        Exception? failure;
        lock (_stateLock)
        {
            if (_isReady) return;
            failure = _unrecoveredIndexFailure;
            task = _readyTcs.Task;
        }

        if (failure != null)
        {
            throw new InvalidOperationException("The resource search index is unavailable", failure);
        }

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

    public async Task<Dictionary<string, int>?> GetPropertyValueResourceCountsAsync(PropertyPool pool, int propertyId,
        IEnumerable<string> valueIds, IReadOnlySet<int>? resourceIds = null)
    {
        if (!IsReady)
        {
            try
            {
                await WaitForReadyAsync(TimeSpan.FromSeconds(1));
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        var valueIndex = _index.ValueIndex.GetValueOrDefault(pool)?.GetValueOrDefault(propertyId);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var valueId in valueIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal))
        {
            var count = 0;
            if (valueIndex?.TryGetValue(NormalizeValue(valueId), out var references) == true)
            {
                // Writers use this same lock. Count in place rather than copying every posting list.
                lock (references)
                {
                    count = resourceIds == null
                        ? references.Count
                        : resourceIds.Count < references.Count
                            ? resourceIds.Count(references.Contains)
                            : references.Count(resourceIds.Contains);
                }
            }

            counts[valueId] = count;
        }

        // A rebuild clears the index; do not present a partial rebuild as authoritative zeros.
        return IsReady ? counts : null;
    }

    public async Task<HashSet<int>?> SearchResourceIdsAsync(ResourceSearchFilterGroup? group)
    {
        if (group == null || group.Disabled)
        {
            return null; // No filter, return null to indicate "all"
        }

        if (GetUnrecoveredIndexFailure() != null)
        {
            return null; // A failed incremental update makes the index unsafe; use the full scan.
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
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Search index unavailable, falling back to full scan");
                return null;
            }
        }

        // The state may have changed while readiness was being awaited.
        if (!IsReady || GetUnrecoveredIndexFailure() != null)
        {
            return null;
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
            foreach (var filter in group.Filters.Where(f => f.IsValid() && !f.Disabled))
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
        // Get the value index for this property
        var poolValueIndex = _index.ValueIndex.GetValueOrDefault(filter.PropertyPool);
        var propValueIndex = poolValueIndex?.GetValueOrDefault(filter.PropertyId);

        // The inner HashSets in ValueIndex are mutated under `lock (resourceIds)`
        // by AddToValueIndex on the writer side; pass-through to a descriptor
        // that enumerates them concurrently throws "Collection was modified
        // during enumeration". Snapshot under the same lock so the descriptor
        // works against a read-only copy.
        Dictionary<string, HashSet<int>>? propValueIndexSnapshot = null;
        if (propValueIndex != null)
        {
            propValueIndexSnapshot = new Dictionary<string, HashSet<int>>(propValueIndex.Count);
            foreach (var kv in propValueIndex)
            {
                HashSet<int> snapshot;
                lock (kv.Value)
                {
                    snapshot = new HashSet<int>(kv.Value);
                }
                propValueIndexSnapshot[kv.Key] = snapshot;
            }
        }

        // Get the range index for this property (with lock for thread safety)
        var poolRangeIndex = _index.RangeIndex.GetValueOrDefault(filter.PropertyPool);
        var propRangeIndex = poolRangeIndex?.GetValueOrDefault(filter.PropertyId);

        List<KeyValuePair<IComparable, HashSet<int>>>? rangeIndexList = null;
        if (propRangeIndex != null)
        {
            lock (propRangeIndex)
            {
                // Snapshot each inner HashSet too — AddToRangeIndex mutates them
                // under the same `lock (sortedList)` we're holding here, but the
                // descriptor reads them outside the lock.
                rangeIndexList = propRangeIndex
                    .Select(kv => new KeyValuePair<IComparable, HashSet<int>>(kv.Key, new HashSet<int>(kv.Value)))
                    .ToList();
            }
        }

        // Get all resource IDs
        HashSet<int> allResourceIds;
        lock (_index.AllResourceIdsLock)
        {
            allResourceIds = new HashSet<int>(_index.AllResourceIds);
        }

        // Use PropertySystem to evaluate the filter on the index
        return PropertySystem.Search.EvaluateOnIndex(
            filter,
            propValueIndexSnapshot,
            rangeIndexList,
            allResourceIds);
    }

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
            PendingUpdateCount = (int)Math.Min(int.MaxValue,
                Math.Max(0, Interlocked.Read(ref _pendingOperationCount))),
            IndexSizes = new Dictionary<string, int>
            {
                ["ValueIndex"] = _index.GetValueIndexEntryCount(),
                ["RangeIndex"] = _index.GetRangeIndexEntryCount()
            }
        };
    }

    #endregion
}
