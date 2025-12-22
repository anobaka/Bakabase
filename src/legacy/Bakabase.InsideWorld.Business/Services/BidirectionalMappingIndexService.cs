using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>
/// Generic singleton service that maintains bidirectional indices for any mapping type.
/// Provides O(1) lookups in both directions.
/// </summary>
/// <typeparam name="TMapping">The mapping entity type</typeparam>
/// <typeparam name="TKey1">The first key type (e.g., MediaLibraryId)</typeparam>
/// <typeparam name="TKey2">The second key type (e.g., ResourceId)</typeparam>
public class BidirectionalMappingIndexService<TMapping, TKey1, TKey2>
    where TKey1 : notnull
    where TKey2 : notnull
{
    private readonly Func<TMapping, TKey1> _getKey1;
    private readonly Func<TMapping, TKey2> _getKey2;
    private readonly Func<Task<IEnumerable<TMapping>>> _loadAllMappings;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private ConcurrentDictionary<TKey1, ImmutableHashSet<TKey2>>? _key1ToKey2;
    private ConcurrentDictionary<TKey2, ImmutableHashSet<TKey1>>? _key2ToKey1;
    private bool _initialized;

    public BidirectionalMappingIndexService(
        Func<TMapping, TKey1> getKey1,
        Func<TMapping, TKey2> getKey2,
        Func<Task<IEnumerable<TMapping>>> loadAllMappings)
    {
        _getKey1 = getKey1;
        _getKey2 = getKey2;
        _loadAllMappings = loadAllMappings;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            var allMappings = (await _loadAllMappings()).ToList();
            _key1ToKey2 = new ConcurrentDictionary<TKey1, ImmutableHashSet<TKey2>>();
            _key2ToKey1 = new ConcurrentDictionary<TKey2, ImmutableHashSet<TKey1>>();

            // Group by keys for efficient bulk initialization
            var byKey1 = allMappings.GroupBy(_getKey1)
                .ToDictionary(g => g.Key, g => g.Select(_getKey2).ToImmutableHashSet());
            var byKey2 = allMappings.GroupBy(_getKey2)
                .ToDictionary(g => g.Key, g => g.Select(_getKey1).ToImmutableHashSet());

            foreach (var kv in byKey1)
            {
                _key1ToKey2[kv.Key] = kv.Value;
            }
            foreach (var kv in byKey2)
            {
                _key2ToKey1[kv.Key] = kv.Value;
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Lock-free add using ConcurrentDictionary.AddOrUpdate with ImmutableHashSet
    /// </summary>
    private void AddInternal(TKey1 key1, TKey2 key2)
    {
        _key1ToKey2!.AddOrUpdate(
            key1,
            _ => ImmutableHashSet.Create(key2),
            (_, existing) => existing.Add(key2));

        _key2ToKey1!.AddOrUpdate(
            key2,
            _ => ImmutableHashSet.Create(key1),
            (_, existing) => existing.Add(key1));
    }

    /// <summary>
    /// Add from mapping entity
    /// </summary>
    public async Task AddAsync(TMapping mapping)
    {
        await EnsureInitializedAsync();
        AddInternal(_getKey1(mapping), _getKey2(mapping));
    }

    /// <summary>
    /// Add multiple mappings
    /// </summary>
    public async Task AddRangeAsync(IEnumerable<TMapping> mappings)
    {
        await EnsureInitializedAsync();
        foreach (var mapping in mappings)
        {
            AddInternal(_getKey1(mapping), _getKey2(mapping));
        }
    }

    /// <summary>
    /// Lock-free remove using CAS pattern
    /// </summary>
    private void RemoveInternal(TKey1 key1, TKey2 key2)
    {
        // Remove from key1ToKey2
        while (true)
        {
            if (!_key1ToKey2!.TryGetValue(key1, out var values))
                break;

            var newSet = values.Remove(key2);
            if (newSet.IsEmpty)
            {
                if (_key1ToKey2.TryRemove(new KeyValuePair<TKey1, ImmutableHashSet<TKey2>>(key1, values)))
                    break;
            }
            else
            {
                if (_key1ToKey2.TryUpdate(key1, newSet, values))
                    break;
            }
        }

        // Remove from key2ToKey1
        while (true)
        {
            if (!_key2ToKey1!.TryGetValue(key2, out var values))
                break;

            var newSet = values.Remove(key1);
            if (newSet.IsEmpty)
            {
                if (_key2ToKey1.TryRemove(new KeyValuePair<TKey2, ImmutableHashSet<TKey1>>(key2, values)))
                    break;
            }
            else
            {
                if (_key2ToKey1.TryUpdate(key2, newSet, values))
                    break;
            }
        }
    }

    /// <summary>
    /// Remove from mapping entity
    /// </summary>
    public async Task RemoveAsync(TMapping mapping)
    {
        await EnsureInitializedAsync();
        RemoveInternal(_getKey1(mapping), _getKey2(mapping));
    }

    /// <summary>
    /// Remove multiple mappings
    /// </summary>
    public async Task RemoveRangeAsync(IEnumerable<TMapping> mappings)
    {
        await EnsureInitializedAsync();
        foreach (var mapping in mappings)
        {
            RemoveInternal(_getKey1(mapping), _getKey2(mapping));
        }
    }

    /// <summary>
    /// Get all Key2 values for a Key1 (O(1))
    /// </summary>
    public async Task<HashSet<TKey2>> GetByKey1Async(TKey1 key1)
    {
        await EnsureInitializedAsync();
        return _key1ToKey2!.TryGetValue(key1, out var values)
            ? values.ToHashSet()
            : [];
    }

    /// <summary>
    /// Get all Key2 values for multiple Key1 values (O(m))
    /// </summary>
    public async Task<HashSet<TKey2>> GetByKey1sAsync(IEnumerable<TKey1> key1s)
    {
        await EnsureInitializedAsync();
        var result = new HashSet<TKey2>();
        foreach (var key1 in key1s)
        {
            if (_key1ToKey2!.TryGetValue(key1, out var values))
            {
                result.UnionWith(values);
            }
        }
        return result;
    }

    /// <summary>
    /// Get all Key1 values for a Key2 (O(1))
    /// </summary>
    public async Task<HashSet<TKey1>> GetByKey2Async(TKey2 key2)
    {
        await EnsureInitializedAsync();
        return _key2ToKey1!.TryGetValue(key2, out var values)
            ? values.ToHashSet()
            : [];
    }

    /// <summary>
    /// Get all Key1 values for multiple Key2 values (O(m))
    /// </summary>
    public async Task<Dictionary<TKey2, HashSet<TKey1>>> GetByKey2sAsync(IEnumerable<TKey2> key2s)
    {
        await EnsureInitializedAsync();
        var result = new Dictionary<TKey2, HashSet<TKey1>>();
        foreach (var key2 in key2s)
        {
            result[key2] = _key2ToKey1!.TryGetValue(key2, out var values)
                ? values.ToHashSet()
                : [];
        }
        return result;
    }
}
