using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Service.Models.Input;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Service.Components.Federation.MetadataReplicas;

/// <summary>
/// Runs the ordinary descriptor search algebra on bounded resource partitions. A descriptor sees
/// all values/scopes for each resource (including missing values); splitting posting lists instead
/// would change NotEquals/IsNull semantics. Matches and sorting stay on disk, never in a library-sized set.
/// </summary>
public sealed partial class MetadataReplicaQuery(MetadataReplicaStore store)
{
    public const int ResourceChunkSize = 256;
    public const int PostingChunkBytes = 4 * 1024 * 1024;
    private static readonly SemaphoreSlim QuerySlots = new(2, 2);

    public Task<ReplicaSearchResult> SearchAsync(ReplicaIdentity identity, ResourceSearch search,
        CancellationToken cancellationToken = default) => RunAsync(identity, (_, _, _, _) => Task.FromResult(search), cancellationToken);

    public Task<ReplicaSearchResult> SearchAsync(ReplicaIdentity identity, ResourceSearchInputModel search,
        CancellationToken cancellationToken = default) => RunAsync(identity,
        (connection, transaction, generation, ct) => CompileAsync(search, connection, transaction, generation, ct), cancellationToken);

    public Task<Dictionary<string, int>> PropertyValueResourceCountsAsync(ReplicaIdentity identity,
        PropertyPool pool, int propertyId, ResourceSearchInputModel search, CancellationToken cancellationToken = default) =>
        WithMatchesAsync(identity, (connection, transaction, generation, ct) =>
            CompileAsync(search with {Page = 1, PageSize = 1}, connection, transaction, generation, ct),
            async (connection, transaction, generation, _, _, ct) =>
            {
                await using var propertyCommand = connection.CreateCommand();
                propertyCommand.Transaction = transaction;
                propertyCommand.CommandText = "SELECT json FROM catalog WHERE generation=$g AND kind='properties' AND key=$key";
                propertyCommand.Parameters.AddWithValue("$g", generation);
                propertyCommand.Parameters.AddWithValue("$key", $"{(int)pool}:{propertyId}");
                if (await propertyCommand.ExecuteScalarAsync(ct) is not string json)
                    throw new NotSupportedException("The replica has no definition for the requested property.");
                var property = ParseProperty(json);
                var counts = property.GetReferenceValueIds().Distinct(StringComparer.Ordinal).ToDictionary(v => v, _ => 0);
                if (counts.Count > 100_000) throw new ReplicaBudgetExceededException("The reference-value count response exceeds its item budget.");
                var normalized = counts.Keys.GroupBy(v => v.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.ToArray());
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT p.value_key,count(DISTINCT p.resource_id) FROM postings p JOIN matches m ON m.id=p.resource_id WHERE p.generation=$g AND p.pool=$pool AND p.property_id=$property AND p.value_key IS NOT NULL GROUP BY p.value_key";
                command.Parameters.AddWithValue("$g", generation);
                command.Parameters.AddWithValue("$pool", (int)pool);
                command.Parameters.AddWithValue("$property", propertyId);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    if (normalized.TryGetValue(reader.GetString(0), out var ids))
                        foreach (var id in ids) counts[id] = checked((int)reader.GetInt64(1));
                return counts;
            }, cancellationToken);

    public async Task<IReadOnlyList<string>> KeywordRecommendationsAsync(ReplicaIdentity identity, string keyword,
        int maxCount = 10, CancellationToken cancellationToken = default)
    {
        if (maxCount is < 1 or > 100 || keyword.Length > 4096) throw new ArgumentOutOfRangeException(nameof(maxCount));
        await QuerySlots.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await store.OpenAsync(identity, cancellationToken);
            using var transaction = connection.BeginTransaction(deferred: true);
            var state = await MetadataReplicaStore.RequireActiveAsync(connection, transaction, cancellationToken);
            var generation = state.ActiveGeneration!;
            var result = new List<string>();
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            await using var libraries = connection.CreateCommand();
            libraries.Transaction = transaction;
            libraries.CommandText = "SELECT json FROM catalog WHERE generation=$g AND kind='mediaLibraries' ORDER BY key";
            libraries.Parameters.AddWithValue("$g", generation);
            await using (var reader = await libraries.ExecuteReaderAsync(cancellationToken))
                while (result.Count < maxCount && await reader.ReadAsync(cancellationToken))
                {
                    var name = JObject.Parse(reader.GetString(0)).GetValue("name", StringComparison.OrdinalIgnoreCase)?.ToObject<string>();
                    // Same case-sensitive media-library match as the ordinary controller.
                    if (name?.Contains(keyword, StringComparison.Ordinal) == true && distinct.Add(name)) result.Add(name);
                }
            if (result.Count == maxCount) return result;
            var propertyMap = await ReadPropertiesAsync(connection, transaction, generation, cancellationToken);
            var filters = (propertyMap.GetValueOrDefault(PropertyPool.Custom)?.Values ?? Enumerable.Empty<Property>())
                .Select(p => (Property: p, Filter: PropertySystem.Property.GetSearchHandler(p.Type).BuildSearchFilterByKeyword(p, keyword)))
                .Where(p => p.Filter != null).ToDictionary(p => p.Property.Id);
            if (filters.Count == 0) return result;
            await using var resources = connection.CreateCommand();
            resources.Transaction = transaction;
            resources.CommandText = "SELECT json FROM resources WHERE generation=$g ORDER BY id";
            resources.Parameters.AddWithValue("$g", generation);
            await using var resourceReader = await resources.ExecuteReaderAsync(cancellationToken);
            while (result.Count < maxCount && await resourceReader.ReadAsync(cancellationToken))
            {
                var json = JObject.Parse(resourceReader.GetString(0));
                var custom = json.GetValue("properties", StringComparison.OrdinalIgnoreCase)?[((int)PropertyPool.Custom).ToString(CultureInfo.InvariantCulture)] as JObject;
                if (custom == null) continue;
                foreach (var entry in custom.Properties())
                {
                    if (!int.TryParse(entry.Name, out var id) || !filters.TryGetValue(id, out var rule)) continue;
                    var handler = PropertySystem.Property.GetSearchHandler(rule.Property.Type);
                    var descriptor = PropertySystem.Property.GetDescriptor(rule.Property.Type);
                    var dbType = StandardValueSystem.GetHandler(descriptor.DbValueType).ExpectedType;
                    var display = StandardValueSystem.GetHandler(descriptor.BizValueType);
                    var values = (entry.Value as JObject)?.GetValue("values", StringComparison.OrdinalIgnoreCase) as JArray;
                    foreach (var value in values?.OfType<JObject>() ?? [])
                    {
                        var token = value.GetValue("value", StringComparison.OrdinalIgnoreCase);
                        var dbValue = token?.Type is null or JTokenType.Null ? null : token.ToObject(dbType);
                        if (dbValue == null || !handler.IsMatch(dbValue, rule.Filter!.Operation, rule.Filter.DbValue)) continue;
                        var text = display.BuildDisplayValue(descriptor.GetBizValue(rule.Property, dbValue));
                        if (!string.IsNullOrEmpty(text) && distinct.Add(text)) result.Add(text);
                        if (result.Count == maxCount) break;
                    }
                    if (result.Count == maxCount) break;
                }
            }
            return result;
        }
        finally { QuerySlots.Release(); }
    }

    private Task<ReplicaSearchResult> RunAsync(ReplicaIdentity identity,
        Func<SqliteConnection, SqliteTransaction, string, CancellationToken, Task<ResourceSearch>> compile,
        CancellationToken ct) => WithMatchesAsync(identity, compile, ReadSearchPageAsync, ct);

    private async Task<TResult> WithMatchesAsync<TResult>(ReplicaIdentity identity,
        Func<SqliteConnection, SqliteTransaction, string, CancellationToken, Task<ResourceSearch>> compile,
        Func<SqliteConnection, SqliteTransaction, string, ResourceSearch, long, CancellationToken, Task<TResult>> read,
        CancellationToken ct)
    {
        await QuerySlots.WaitAsync(ct);
        try
        {
            await using var connection = await store.OpenAsync(identity, ct);
            using var transaction = connection.BeginTransaction(deferred: true);
            var state = await MetadataReplicaStore.RequireActiveAsync(connection, transaction, ct);
            var generation = state.ActiveGeneration!;
            var search = await compile(connection, transaction, generation, ct);
            if (search.Owner != null || search.DeviceIds != null)
                throw new ArgumentException("Resolve device scopes before searching a metadata replica.");
            if (search.PageIndex < 1 || search.PageSize is < 1 or > 100)
                throw new ArgumentOutOfRangeException(nameof(search), "Replica search pages must contain 1–100 resources.");
            var filters = ValidateGroup(search.Group).ToArray();
            var required = filters.Select(f => (f.PropertyPool, f.PropertyId)).ToHashSet();
            var tags = 0;
            foreach (var tag in search.Tags ?? [])
            {
                if (!Enum.IsDefined(tag)) throw new ArgumentException("Unknown resource tag.");
                tags |= (int)tag;
            }
            _ = BuildOrder(search);
            await MetadataReplicaStore.ExecuteAsync(connection, transaction,
                "CREATE TEMP TABLE matches(id INTEGER PRIMARY KEY) WITHOUT ROWID", ct);
            var afterId = 0;
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO matches(id) VALUES($id)";
            insert.Parameters.AddWithValue("$id", 0);
            if (required.Count == 0)
                await MetadataReplicaStore.ExecuteAsync(connection, transaction,
                    "INSERT INTO matches(id) SELECT id FROM resources WHERE generation=$g AND (tags & $tags)=$tags", ct,
                    ("$g", generation), ("$tags", tags));
            while (required.Count > 0)
            {
                var ids = await ReadChunkAsync(connection, transaction, generation, afterId, tags, ct);
                if (ids.Count == 0) break;
                afterId = ids[^1];
                var indices = await ReadIndicesAsync(connection, transaction, generation, ids, required, ct);
                var matches = EvaluateGroup(search.Group, indices, ids.ToHashSet()) ?? ids.ToHashSet();
                foreach (var id in matches)
                {
                    ct.ThrowIfCancellationRequested();
                    insert.Parameters["$id"].Value = id;
                    await insert.ExecuteNonQueryAsync(ct);
                }
            }
            return await read(connection, transaction, generation, search, state.ActiveSequence, ct);
        }
        finally { QuerySlots.Release(); }
    }

    private static async Task<ReplicaSearchResult> ReadSearchPageAsync(SqliteConnection connection,
        SqliteTransaction transaction, string generation, ResourceSearch search, long sequence, CancellationToken ct)
    {
        var order = BuildOrder(search);
        await using var countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = "SELECT count(*) FROM matches";
        var count = (long)(await countCommand.ExecuteScalarAsync(ct))!;
        await using var page = connection.CreateCommand();
        page.Transaction = transaction;
        page.CommandText = $"SELECT r.json FROM matches m JOIN resources r ON r.generation=$g AND r.id=m.id ORDER BY {order} LIMIT $limit OFFSET $offset";
        page.Parameters.AddWithValue("$g", generation);
        page.Parameters.AddWithValue("$limit", search.PageSize);
        page.Parameters.AddWithValue("$offset", ((long)search.PageIndex - 1) * search.PageSize);
        var result = new List<string>();
        long bytes = 0;
        await using var reader = await page.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var json = reader.GetString(0);
            bytes += Encoding.UTF8.GetByteCount(json);
            if (bytes > MetadataReplicaStore.MaxBatchBytes)
                throw new ReplicaBudgetExceededException("Search page exceeds its byte budget; choose a smaller page size.");
            result.Add(json);
        }
        return new ReplicaSearchResult(result, count, search.PageIndex, search.PageSize, sequence);
    }

    private static async Task<List<int>> ReadChunkAsync(SqliteConnection connection, SqliteTransaction transaction,
        string generation, int afterId, int tags, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id,posting_bytes FROM resources WHERE generation=$g AND id>$after AND (tags & $tags)=$tags ORDER BY id LIMIT $limit";
        command.Parameters.AddWithValue("$g", generation);
        command.Parameters.AddWithValue("$after", afterId);
        command.Parameters.AddWithValue("$tags", tags);
        command.Parameters.AddWithValue("$limit", ResourceChunkSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<int>();
        long bytes = 0;
        while (await reader.ReadAsync(ct))
        {
            var size = reader.GetInt64(1);
            if (size > PostingChunkBytes) throw new ReplicaBudgetExceededException("A resource exceeds the query posting budget.");
            if (result.Count > 0 && bytes + size > PostingChunkBytes) break;
            result.Add(reader.GetInt32(0));
            bytes += size;
        }
        return result;
    }

    private sealed class PropertyIndex
    {
        public Dictionary<string, HashSet<int>> Values { get; } = new(StringComparer.Ordinal);
        public SortedList<IComparable, HashSet<int>> Ranges { get; } = new();
    }

    private static async Task<Dictionary<(PropertyPool, int), PropertyIndex>> ReadIndicesAsync(SqliteConnection connection,
        SqliteTransaction transaction, string generation, List<int> ids, HashSet<(PropertyPool, int)> required, CancellationToken ct)
    {
        var indices = new Dictionary<(PropertyPool, int), PropertyIndex>();
        if (required.Count == 0) return indices;
        var idSet = ids.ToHashSet();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT resource_id,pool,property_id,value_key,range_type,range_value FROM postings WHERE generation=$g AND resource_id>=$first AND resource_id<=$last ORDER BY resource_id,ordinal";
        command.Parameters.AddWithValue("$g", generation);
        command.Parameters.AddWithValue("$first", ids[0]);
        command.Parameters.AddWithValue("$last", ids[^1]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetInt32(0);
            var key = ((PropertyPool)reader.GetInt32(1), reader.GetInt32(2));
            if (!idSet.Contains(id) || !required.Contains(key)) continue;
            if (!indices.TryGetValue(key, out var index)) indices[key] = index = new PropertyIndex();
            if (!reader.IsDBNull(3))
            {
                var value = reader.GetString(3);
                if (!index.Values.TryGetValue(value, out var resources)) index.Values[value] = resources = [];
                resources.Add(id);
            }
            if (!reader.IsDBNull(4))
            {
                var range = new ReplicaRangeValue(reader.GetString(4), reader.GetString(5)).ToComparable();
                if (!index.Ranges.TryGetValue(range, out var resources)) index.Ranges[range] = resources = [];
                resources.Add(id);
            }
        }
        return indices;
    }

    private static HashSet<int>? EvaluateGroup(ResourceSearchFilterGroup? group,
        Dictionary<(PropertyPool, int), PropertyIndex> indices, HashSet<int> all)
    {
        if (group == null || group.Disabled) return null;
        var results = new List<HashSet<int>?>();
        foreach (var filter in group.Filters?.Where(f => !f.Disabled && f.IsValid()) ?? [])
        {
            indices.TryGetValue((filter.PropertyPool, filter.PropertyId), out var index);
            var values = index?.Values ?? new Dictionary<string, HashSet<int>>();
            List<KeyValuePair<IComparable, HashSet<int>>>? ranges = index?.Ranges.ToList() ?? [];
            if (filter.Operation == SearchOperation.NotEquals && ranges.Count == 0) ranges = null;
            var result = PropertySystem.Search.EvaluateOnIndex(filter, values, ranges, all);
            // A valid active filter unsupported by the index must never silently become "all".
            if (result == null) throw new NotSupportedException($"The property descriptor cannot index {filter.Property.Type}/{filter.Operation}.");
            results.Add(result);
        }
        foreach (var child in group.Groups?.Where(g => !g.Disabled) ?? []) results.Add(EvaluateGroup(child, indices, all));
        if (results.Count == 0) return null;
        if (group.Combinator == SearchCombinator.Or)
        {
            if (results.Any(r => r == null)) return null;
            var union = new HashSet<int>();
            foreach (var result in results) union.UnionWith(result!);
            return union;
        }
        var constrained = results.Where(r => r != null).OrderBy(r => r!.Count).ToArray();
        if (constrained.Length == 0) return null;
        var intersection = new HashSet<int>(constrained[0]!);
        foreach (var result in constrained.Skip(1)) intersection.IntersectWith(result!);
        return intersection;
    }

    private static IEnumerable<ResourceSearchFilter> ValidateGroup(ResourceSearchFilterGroup? group, int depth = 0)
    {
        if (group == null || group.Disabled) yield break;
        if (depth > 32) throw new ArgumentException("Search groups are nested too deeply.");
        if (!Enum.IsDefined(group.Combinator)) throw new ArgumentException("Unknown search combinator.");
        foreach (var filter in group.Filters?.Where(f => !f.Disabled) ?? [])
        {
            if (filter.Owner != null) throw new ArgumentException("Resolve filter owners before searching a metadata replica.");
            if (!Enum.IsDefined(filter.PropertyPool) || !Enum.IsDefined(filter.Operation) || filter.Property == null ||
                PropertySystem.Property.TryGetDescriptor(filter.Property.Type) is not IPropertyIndexSearcher ||
                PropertySystem.Property.TryGetSearchHandler(filter.Property.Type)?.SearchOperations.ContainsKey(filter.Operation) != true)
                throw new NotSupportedException("Unknown property or unsupported search operation in replica search.");
            if (filter.IsValid()) yield return filter;
        }
        foreach (var child in group.Groups ?? [])
            foreach (var filter in ValidateGroup(child, depth + 1)) yield return filter;
    }

    private static string BuildOrder(ResourceSearch search)
    {
        if (search.Orders?.Length is not > 0) return $"(r.tags & {(int)ResourceTag.Pinned}) DESC,r.id DESC";
        var terms = new List<string>();
        foreach (var order in search.Orders)
        {
            var direction = order.Asc ? " ASC" : " DESC";
            switch (order.Property)
            {
                case ResourceSearchSortableProperty.AddDt: terms.Add("r.created" + direction); break;
                case ResourceSearchSortableProperty.FileCreateDt:
                    terms.Add("(r.path IS NULL OR r.path='') ASC"); terms.Add("r.file_created" + direction); break;
                case ResourceSearchSortableProperty.FileModifyDt:
                    terms.Add("(r.path IS NULL OR r.path='') ASC"); terms.Add("r.file_modified" + direction); break;
                case ResourceSearchSortableProperty.Filename:
                    terms.Add("(r.file_name IS NULL OR r.file_name='') ASC"); terms.Add("r.file_name COLLATE replica_filename" + direction); break;
                case ResourceSearchSortableProperty.PlayedAt: terms.Add("r.played" + direction); break;
                case ResourceSearchSortableProperty.HealthScore:
                    terms.Add("(r.health IS NULL) ASC"); terms.Add("r.health COLLATE replica_decimal" + direction); break;
                default: throw new ArgumentException("Unknown resource sort order.");
            }
        }
        // Stable pagination when two owner resources have equal sort keys.
        terms.Add("r.id DESC");
        return string.Join(',', terms);
    }

    private static async Task<ResourceSearch> CompileAsync(ResourceSearchInputModel input, SqliteConnection connection,
        SqliteTransaction transaction, string generation, CancellationToken ct)
    {
        if (input.Owner != null || input.DeviceIds != null) throw new ArgumentException("Resolve search device scopes first.");
        var properties = await ReadPropertiesAsync(connection, transaction, generation, ct,
            string.IsNullOrEmpty(input.Keyword) ? RequiredPropertyKeys(input.Group).ToHashSet() : null);
        ValidateInput(input.Group, properties);
        var domain = new ResourceSearch
        {
            Group = input.Group == null ? null : Extensions.ResourceSearchExtensions.ToDomainModel(input.Group, properties),
            Orders = input.Orders, PageIndex = input.Page > 0 ? input.Page : 1,
            PageSize = input.PageSize > 0 ? input.PageSize : 20, Tags = input.Tags
        };
        if (!string.IsNullOrEmpty(input.Keyword))
        {
            var filename = properties.GetValueOrDefault(PropertyPool.Internal)?.GetValueOrDefault((int)ResourceProperty.Filename)
                           ?? PropertySystem.Builtin.TryGet(ResourceProperty.Filename)
                           ?? throw new NotSupportedException("Filename search is unavailable.");
            var keyword = new ResourceSearchFilterGroup
            {
                Combinator = SearchCombinator.Or,
                Filters = [new ResourceSearchFilter {Property = filename, PropertyPool = filename.Pool,
                    PropertyId = filename.Id, Operation = SearchOperation.Contains, DbValue = input.Keyword}]
            };
            foreach (var property in properties.GetValueOrDefault(PropertyPool.Custom)?.Values ?? Enumerable.Empty<Property>())
            {
                var filter = PropertySystem.Property.TryGetSearchHandler(property.Type)?.BuildSearchFilterByKeyword(property, input.Keyword);
                if (filter != null) keyword.Filters.Add(filter);
            }
            domain.Group = domain.Group == null ? keyword : new ResourceSearchFilterGroup
            {
                Combinator = SearchCombinator.And, Groups = [domain.Group, keyword]
            };
        }
        return domain;
    }

    private static async Task<Dictionary<PropertyPool, Dictionary<int, Property>>> ReadPropertiesAsync(
        SqliteConnection connection, SqliteTransaction transaction, string generation, CancellationToken ct,
        HashSet<string>? requiredKeys = null)
    {
        var properties = new Dictionary<PropertyPool, Dictionary<int, Property>>();
        if (requiredKeys?.Count == 0) return properties;
        if (requiredKeys != null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT json FROM catalog WHERE generation=$g AND kind='properties' AND key=$key";
            command.Parameters.AddWithValue("$g", generation);
            command.Parameters.AddWithValue("$key", "");
            long propertyBytes = 0;
            foreach (var key in requiredKeys)
            {
                command.Parameters["$key"].Value = key;
                if (await command.ExecuteScalarAsync(ct) is not string json) continue;
                propertyBytes += Encoding.UTF8.GetByteCount(json);
                if (propertyBytes > MetadataReplicaStore.MaxBatchBytes) throw new ReplicaBudgetExceededException("Property definitions exceed the query catalog budget.");
                var property = ParseProperty(json);
                if (!properties.TryGetValue(property.Pool, out var byId)) properties[property.Pool] = byId = [];
                byId[property.Id] = property;
            }
            return properties;
        }
        string? after = null;
        long bytes = 0;
        while (true)
        {
            var page = await MetadataReplicaStore.ReadCatalogPageAsync(connection, transaction, generation, "properties", after, 100, ct);
            if (page.Count == 0) break;
            foreach (var row in page)
            {
                bytes += Encoding.UTF8.GetByteCount(row.Json);
                if (bytes > MetadataReplicaStore.MaxBatchBytes) throw new ReplicaBudgetExceededException("Property definitions exceed the query catalog budget.");
                var property = ParseProperty(row.Json);
                if (!properties.TryGetValue(property.Pool, out var byId)) properties[property.Pool] = byId = [];
                byId[property.Id] = property;
            }
            after = page[^1].Key;
        }
        return properties;
    }

    private static IEnumerable<string> RequiredPropertyKeys(ResourceSearchFilterGroupInputModel? group, int depth = 0)
    {
        if (group == null || group.Disabled) yield break;
        if (depth > 32) throw new ArgumentException("Search groups are nested too deeply.");
        foreach (var filter in group.Filters?.Where(f => !f.Disabled && f.PropertyPool != null && f.PropertyId != null) ?? [])
            yield return $"{(int)filter.PropertyPool!.Value}:{filter.PropertyId}";
        foreach (var child in group.Groups ?? [])
            foreach (var key in RequiredPropertyKeys(child, depth + 1)) yield return key;
    }

    private static Property ParseProperty(string value)
    {
        var json = JObject.Parse(value);
        var pool = json.GetValue("pool", StringComparison.OrdinalIgnoreCase)!.ToObject<PropertyPool>();
        var id = json.GetValue("id", StringComparison.OrdinalIgnoreCase)!.ToObject<int>();
        var type = json.GetValue("type", StringComparison.OrdinalIgnoreCase)!.ToObject<PropertyType>();
        var descriptor = PropertySystem.Property.TryGetDescriptor(type) ?? throw new NotSupportedException("Unknown replica property type.");
        var optionJson = json.GetValue("options", StringComparison.OrdinalIgnoreCase);
        var options = descriptor.OptionsType != null && optionJson?.Type is not (null or JTokenType.Null)
            ? optionJson!.ToObject(descriptor.OptionsType) : null;
        return new Property(pool, id, type, json.GetValue("name", StringComparison.OrdinalIgnoreCase)?.ToObject<string>(), options);
    }

    private static void ValidateInput(ResourceSearchFilterGroupInputModel? group,
        Dictionary<PropertyPool, Dictionary<int, Property>> properties, int depth = 0)
    {
        if (group == null || group.Disabled) return;
        if (depth > 32 || !Enum.IsDefined(group.Combinator)) throw new ArgumentException("Invalid search group.");
        foreach (var filter in group.Filters?.Where(f => !f.Disabled) ?? [])
        {
            if (filter.Owner != null) throw new ArgumentException("Resolve filter device scopes first.");
            if (filter.PropertyPool == null || filter.PropertyId == null || filter.Operation == null) continue;
            var property = properties.GetValueOrDefault(filter.PropertyPool.Value)?.GetValueOrDefault(filter.PropertyId.Value);
            if (property == null || PropertySystem.Property.TryGetSearchHandler(property.Type)?.SearchOperations.ContainsKey(filter.Operation.Value) != true)
                throw new NotSupportedException("The replica has no definition for the requested property or operation.");
        }
        foreach (var child in group.Groups ?? []) ValidateInput(child, properties, depth + 1);
    }
}
