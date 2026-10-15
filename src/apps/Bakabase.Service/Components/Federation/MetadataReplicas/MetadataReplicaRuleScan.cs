using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Search.Models.Db;
using Bakabase.Service.Models.Input;
using Newtonsoft.Json;

namespace Bakabase.Service.Components.Federation.MetadataReplicas;

public sealed record ReplicaRuleScan(string Generation, int UpperId, int AfterId, int[] Ids, bool Complete);

public sealed partial class MetadataReplicaQuery
{
    /// <summary>One bounded partition of a rule's resources, in stable ID order. Values and
    /// postings stay on disk; restarting a large job never needs a library-sized match list.</summary>
    public async Task<ReplicaRuleScan> ScanRuleIdsAsync(ReplicaIdentity identity, string? searchJson,
        string? expectedGeneration, int afterId, int? upperId, CancellationToken ct)
    {
        await QuerySlots.WaitAsync(ct);
        try
        {
            await using var connection = await store.OpenAsync(identity, ct);
            using var transaction = connection.BeginTransaction(deferred: true);
            var status = await MetadataReplicaStore.RequireActiveAsync(connection, transaction, ct);
            var generation = status.ActiveGeneration!;
            if (expectedGeneration != null && expectedGeneration != generation)
                throw new ReplicaCheckpointConflictException();
            await using var max = connection.CreateCommand();
            max.Transaction = transaction;
            max.CommandText = "SELECT coalesce(max(id),0) FROM resources WHERE generation=$g";
            max.Parameters.AddWithValue("$g", generation);
            var upper = upperId ?? checked((int)(long)(await max.ExecuteScalarAsync(ct))!);
            var db = JsonConvert.DeserializeObject<ResourceSearchDbModel>(searchJson ?? "{}") ?? new();
            ResourceSearchFilterGroupInputModel? Group(ResourceSearchFilterGroupDbModel? g) => g == null ? null : new()
            {
                Disabled = g.Disabled, Combinator = g.Combinator,
                Groups = g.Groups?.Select(child => Group(child)!).ToList(),
                Filters = g.Filters?.Select(f => new ResourceSearchFilterInputModel
                {
                    PropertyPool = f.PropertyPool, PropertyId = f.PropertyId, Operation = f.Operation,
                    DbValue = f.Value, Disabled = f.Disabled
                }).ToList()
            };
            var search = await CompileAsync(new ResourceSearchInputModel
            {
                Owner = db.Owner, DeviceIds = db.DeviceIds, Group = Group(db.Group), Keyword = db.Keyword,
                Tags = db.Tags, Page = 1, PageSize = 100
            }, connection, transaction, generation, ct);
            var required = ValidateGroup(search.Group).Select(f => (f.PropertyPool, f.PropertyId)).ToHashSet();
            var tags = 0;
            foreach (var tag in search.Tags ?? [])
            {
                if (!Enum.IsDefined(tag)) throw new ArgumentException("Unknown resource tag.");
                tags |= (int)tag;
            }
            var candidates = (await ReadChunkAsync(connection, transaction, generation, afterId, tags, ct))
                .Where(id => id <= upper).ToList();
            if (candidates.Count == 0) return new(generation, upper, upper, [], true);
            var indices = await ReadIndicesAsync(connection, transaction, generation, candidates, required, ct);
            var matches = EvaluateGroup(search.Group, indices, candidates.ToHashSet()) ?? candidates.ToHashSet();
            return new(generation, upper, candidates[^1], candidates.Where(matches.Contains).ToArray(), candidates[^1] >= upper);
        }
        finally { QuerySlots.Release(); }
    }
}
