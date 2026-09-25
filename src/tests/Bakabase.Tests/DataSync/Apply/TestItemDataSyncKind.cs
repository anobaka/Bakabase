using System.Globalization;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Ordering;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// The <c>testItem</c> kind's adapter over definitions held in memory, as the custom property kind stands over its
/// service: local keys are ids, children have usage (resources) and the entity values (a count), the subtype can be
/// changed (options rebuilt with fresh ids, F73) and order is kept. A test edits <see cref="Definitions"/> directly as
/// any local writer would. It is not transactional: tests of rollbacks use the extension group kind.
/// </summary>
internal sealed class TestItemDataSyncKind : IDataSyncKind
{
    private int _nextId;

    public TestItemCodec TestCodec { get; } = TestItemCodec.Instance;
    public IDataSyncKindCodec Codec => TestCodec;

    public Dictionary<string, TestItemContent> Definitions { get; } = new(StringComparer.Ordinal);
    public List<string> Order { get; } = [];

    /// <summary>Resources per child id, per entity (a missing child is unused).</summary>
    public Dictionary<string, Dictionary<string, int>> Usage { get; } = new(StringComparer.Ordinal);

    /// <summary>Values per entity.</summary>
    public Dictionary<string, int> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>Values a subtype change would lose, per entity (the preview's LossyCount).</summary>
    public Dictionary<string, int> Lossy { get; } = new(StringComparer.Ordinal);

    /// <summary>Thrown by <see cref="ApplyAsync"/> for the first operation it returns an exception for.</summary>
    public Func<ApplyOperation, Exception?>? FailOn { get; set; }

    /// <summary>What an update does to the stored content (an unmodelled normalization, §6.4).</summary>
    public Func<TestItemContent, TestItemContent>? NormalizeOnWrite { get; set; }

    public int CacheResets { get; private set; }
    public List<ApplyOperation> Applied { get; } = [];

    public string Add(TestItemContent content)
    {
        var key = NextKey();
        Definitions[key] = content;
        Order.Add(key);
        return key;
    }

    public TestItemContent this[string localKey] => Definitions[localKey];

    public string KeyOf(string name) => Definitions.Single(d => d.Value.Name == name).Key;

    public void Use(string localKey, string childId, int resources)
    {
        if (!Usage.TryGetValue(localKey, out var usage)) Usage[localKey] = usage = new Dictionary<string, int>();
        usage[childId] = resources;
    }

    private string NextKey() => (++_nextId).ToString(CultureInfo.InvariantCulture);

    public Task<IReadOnlyList<LocalEntity>> ReadAsync(IReadOnlyCollection<string>? localKeys, CancellationToken ct)
    {
        IReadOnlyList<LocalEntity> result = Order
            .Where(k => localKeys is null || localKeys.Contains(k))
            .Select(k => new LocalEntity(k, null, Order.IndexOf(k), TestCodec.Write(Definitions[k])))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<string, JsonObject>> CapturePreImageAsync(IReadOnlyCollection<string> localKeys,
        CancellationToken ct)
    {
        IReadOnlyDictionary<string, JsonObject> result = localKeys.Where(Definitions.ContainsKey)
            .ToDictionary(k => k, k => new JsonObject
            {
                ["content"] = TestCodec.Write(Definitions[k]),
                ["order"] = Order.IndexOf(k),
            });
        return Task.FromResult(result);
    }

    public Task<ApplyBatchOutcome> ApplyAsync(ApplyBatch batch, CancellationToken ct)
    {
        var created = new Dictionary<string, string>();
        var changed = new HashSet<string>();
        foreach (var op in batch.Operations)
        {
            if (FailOn?.Invoke(op) is { } failure) throw failure;
            Applied.Add(op);
            switch (op)
            {
                case CreateEntityOperation create:
                {
                    var key = NextKey();
                    Definitions[key] = TestCodec.ReadLocal(create.Content);
                    Order.Add(key);
                    created[create.ItemId] = key;
                    break;
                }
                case UpdateEntityOperation update:
                    if (!HashHolds(update.LocalKey, update.ExpectedLocalHash))
                    {
                        changed.Add(update.ItemId);
                        break;
                    }

                    var content = TestCodec.ReadLocal(update.MergedContent);
                    Definitions[update.LocalKey] = NormalizeOnWrite?.Invoke(content) ?? content;
                    break;
                case BindOnlyOperation:
                    break;
                case DeleteEntityOperation delete:
                    if (!HashHolds(delete.LocalKey, delete.ExpectedLocalHash))
                    {
                        changed.Add(delete.ItemId);
                        break;
                    }

                    Remove(delete.LocalKey);
                    break;
                case ChangeSubtypeOperation subtype:
                    if (!HashHolds(subtype.LocalKey, subtype.ExpectedLocalHash))
                    {
                        changed.Add(subtype.ItemId);
                        break;
                    }

                    ChangeSubtype(subtype.LocalKey, subtype.Subtype);
                    break;
                default:
                    throw new NotSupportedException(op.GetType().Name);
            }
        }

        return Task.FromResult(new ApplyBatchOutcome(created, changed));
    }

    private bool HashHolds(string localKey, string expected) =>
        Definitions.TryGetValue(localKey, out var content) && ContentHash.Of(TestCodec.Write(content)) == expected;

    /// <summary>Like <c>ChangeType</c> (F73): the options are rebuilt with fresh ids; values are converted.</summary>
    private void ChangeSubtype(string localKey, string subtype)
    {
        var content = Definitions[localKey];
        Definitions[localKey] = content.With(type: subtype,
            children: content.Children.Select(c => new TestChild(c.Id + "#" + subtype, c.Label)).ToList());
        Usage.Remove(localKey);
    }

    public Task<IReadOnlyDictionary<string, EntityUsage>> GetUsageAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> childIdsByLocalKey, CancellationToken ct)
    {
        IReadOnlyDictionary<string, EntityUsage> result = childIdsByLocalKey.ToDictionary(e => e.Key, e =>
        {
            var usage = Usage.GetValueOrDefault(e.Key);
            return new EntityUsage(Values.GetValueOrDefault(e.Key),
                e.Value.Distinct().ToDictionary(c => c, c => usage?.GetValueOrDefault(c) ?? 0));
        });
        return Task.FromResult(result);
    }

    public Task RestoreAsync(string localKey, JsonObject preImage, CancellationToken ct)
    {
        Definitions[localKey] = TestCodec.ReadLocal(preImage["content"]!.AsObject());
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string localKey, CancellationToken ct)
    {
        Remove(localKey);
        return Task.CompletedTask;
    }

    public void Remove(string localKey)
    {
        Definitions.Remove(localKey);
        Order.Remove(localKey);
        Usage.Remove(localKey);
        Values.Remove(localKey);
    }

    public void ResetCaches() => CacheResets++;

    public Task<IReadOnlyList<string>> ReadOrderAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Order.ToList());

    public Task ApplyOrderAsync(IReadOnlyList<string> syncedLocalKeysInSharedOrder, CancellationToken ct)
    {
        var placed = DataSyncOrderPlanner.Place(Order, syncedLocalKeysInSharedOrder);
        Order.Clear();
        Order.AddRange(placed);
        return Task.CompletedTask;
    }

    public Task ChangeSubtypeAsync(string localKey, string subtype, CancellationToken ct)
    {
        ChangeSubtype(localKey, subtype);
        return Task.CompletedTask;
    }

    public Task<DataSyncTypeChangePreview> PreviewSubtypeChangeAsync(string localKey, string subtype,
        CancellationToken ct) =>
        Task.FromResult(new DataSyncTypeChangePreview(Definitions[localKey].Type ?? "", subtype,
            Values.GetValueOrDefault(localKey), Values.GetValueOrDefault(localKey), Lossy.GetValueOrDefault(localKey), []));

    public Task<IReadOnlyDictionary<string, string>> ReadRawHashesAsync(CancellationToken ct)
    {
        IReadOnlyDictionary<string, string> result =
            Definitions.ToDictionary(e => e.Key, e => ContentHash.Of(TestCodec.Write(e.Value)));
        return Task.FromResult(result);
    }
}
