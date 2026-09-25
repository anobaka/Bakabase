using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// A kind whose definitions live in memory, so a test edits them directly as any local writer would. Content is
/// <c>{"children":[{"id","label","parent"?,"group"?}],"name",…}</c>; the codec compares children as labels (ids and
/// local order are local only), which is all Refresh needs from a real kind.
/// </summary>
internal sealed class MemoryDataSyncKind : IDataSyncKind
{
    public MemoryDataSyncKind(string kind = DataSyncKindIds.CustomProperty, bool hasOrder = false)
    {
        MemoryCodec = new MemoryCodec(kind, hasOrder);
    }

    public MemoryCodec MemoryCodec { get; }
    public IDataSyncKindCodec Codec => MemoryCodec;

    /// <summary>Definitions by local key; <see cref="Order"/> holds the local order.</summary>
    public Dictionary<string, MemoryDefinition> Definitions { get; } = new(StringComparer.Ordinal);

    public List<string> Order { get; } = [];

    /// <summary>Every key set <see cref="ReadAsync"/> was asked for (null: all).</summary>
    public List<IReadOnlyCollection<string>?> Reads { get; } = [];

    /// <summary>What an update does to the content before it is stored (an unmodelled normalization, §6.4).</summary>
    public Func<JsonObject, JsonObject>? NormalizeOnWrite { get; set; }

    public MemoryDefinition Add(string localKey, string name, params (string Id, string Label)[] children)
    {
        var definition = new MemoryDefinition(name, children.Select(c => new MemoryChild(c.Id, c.Label)).ToList());
        Definitions[localKey] = definition;
        Order.Add(localKey);
        return definition;
    }

    public void Remove(string localKey)
    {
        Definitions.Remove(localKey);
        Order.Remove(localKey);
    }

    public Task<IReadOnlyList<LocalEntity>> ReadAsync(IReadOnlyCollection<string>? localKeys, CancellationToken ct)
    {
        Reads.Add(localKeys?.ToList());
        IReadOnlyList<LocalEntity> result = Order
            .Where(k => localKeys is null || localKeys.Contains(k))
            .Select(k => new LocalEntity(k, Definitions[k].Fingerprint, Order.IndexOf(k), Definitions[k].ToContent(),
                Definitions[k].Unreadable))
            .ToList();
        return Task.FromResult(result);
    }

    /// <summary>Runs as Refresh reads the raw hashes: something happening while a Refresh is under way.</summary>
    public Action? OnReadRawHashes { get; set; }

    public Task<IReadOnlyDictionary<string, string>> ReadRawHashesAsync(CancellationToken ct)
    {
        OnReadRawHashes?.Invoke();
        IReadOnlyDictionary<string, string> result = Definitions.ToDictionary(e => e.Key, e => ContentHash.Of(
            new JsonArray(e.Value.ToContent(), JsonValue.Create(e.Value.Fingerprint), JsonValue.Create(e.Value.Unreadable))));
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<string>> ReadOrderAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Order.ToList());

    public Task<ApplyBatchOutcome> ApplyAsync(ApplyBatch batch, CancellationToken ct)
    {
        var created = new Dictionary<string, string>();
        var changed = new HashSet<string>();
        foreach (var operation in batch.Operations)
        {
            switch (operation)
            {
                case UpdateEntityOperation update:
                    if (ContentHash.Of(Definitions[update.LocalKey].ToContent()) != update.ExpectedLocalHash)
                    {
                        changed.Add(update.ItemId);
                        break;
                    }

                    var content = NormalizeOnWrite?.Invoke((JsonObject) update.MergedContent.DeepClone()) ??
                                  update.MergedContent;
                    Definitions[update.LocalKey] = MemoryDefinition.FromContent(content, Definitions[update.LocalKey]);
                    break;
                default:
                    throw new NotSupportedException(operation.GetType().Name);
            }
        }

        return Task.FromResult(new ApplyBatchOutcome(created, changed));
    }

    public Task<IReadOnlyDictionary<string, JsonObject>> CapturePreImageAsync(IReadOnlyCollection<string> localKeys,
        CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, EntityUsage>> GetUsageAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> childIdsByLocalKey, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task RestoreAsync(string localKey, JsonObject preImage, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task DeleteAsync(string localKey, CancellationToken ct) => throw new NotSupportedException();

    public void ResetCaches()
    {
    }

    public Task ApplyOrderAsync(IReadOnlyList<string> syncedLocalKeysInSharedOrder, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task ChangeSubtypeAsync(string localKey, string subtype, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<DataSyncTypeChangePreview> PreviewSubtypeChangeAsync(string localKey, string subtype,
        CancellationToken ct) => throw new NotSupportedException();
}

internal sealed record MemoryChild(string Id, string Label, string? Parent = null);

internal sealed class MemoryDefinition(string name, List<MemoryChild> children)
{
    public string Name { get; set; } = name;
    public List<MemoryChild> Children { get; set; } = children;

    /// <summary>Other top-level members, verbatim (settings, a future member…).</summary>
    public JsonObject Extra { get; set; } = new();

    public string? Fingerprint { get; set; }
    public bool Unreadable { get; set; }

    public JsonObject ToContent()
    {
        if (Unreadable) return new JsonObject {["name"] = Name};
        var content = (JsonObject) Extra.DeepClone();
        content["children"] = new JsonArray(Children.Select(c =>
        {
            var child = new JsonObject {["id"] = c.Id, ["label"] = c.Label};
            if (c.Parent is not null) child["parent"] = c.Parent;
            return (JsonNode?) child;
        }).ToArray());
        content["name"] = Name;
        return content;
    }

    public static MemoryDefinition FromContent(JsonObject content, MemoryDefinition? previous = null)
    {
        var extra = (JsonObject) content.DeepClone();
        extra.Remove("children");
        extra.Remove("name");
        return new MemoryDefinition(content["name"]!.GetValue<string>(),
            (content["children"] as JsonArray ?? []).Select(c => new MemoryChild(c!["id"]!.GetValue<string>(),
                c["label"]!.GetValue<string>(), c["parent"]?.GetValue<string>())).ToList())
        {
            Extra = extra,
            Fingerprint = previous?.Fingerprint,
        };
    }
}

/// <summary>
/// The memory kind's codec. Publish removes overlay children (with their subtrees), withholds children with an empty
/// id or label, drops every child with childrenLocal, and holds content marked <c>"tooBig":true</c>. The comparison
/// form keeps labels (sorted, ids and order dropped) and every other member, plus the order key; bumping
/// <see cref="ComparisonFormVersion"/> adds <c>"formVersion"</c> to it.
/// </summary>
internal sealed class MemoryCodec(string kind, bool hasOrder) : IDataSyncKindCodec
{
    private static readonly HashSet<string> Known = ["children", "name", "tooBig", "settings", "childrenLocal", "type"];

    public DataSyncKindDescriptor Descriptor { get; private set; } =
        new(kind, 1, [], typeof(JsonObject), false, hasOrder, true, true, "child");

    /// <summary>The content schema this build reads; raising it simulates an upgrade of the build (§8.4 condition 6).</summary>
    public int SchemaVersion
    {
        get => Descriptor.SchemaVersion;
        set => Descriptor = Descriptor with { SchemaVersion = value };
    }

    public int ComparisonFormVersion { get; set; } = 1;

    public JsonObject Upgrade(JsonObject content, int fromSchemaVersion) =>
        fromSchemaVersion > SchemaVersion ? throw new DataSyncHeldException(DataSyncHeldReason.NewerSchema) : content;

    public CodecReadResult Read(JsonObject content, DataSyncLimits limits)
    {
        if (content["name"] is not JsonValue) return new CodecReadResult(null, DataSyncHeldReason.Invalid, ["name"], []);
        var known = new JsonObject();
        JsonObject? unknown = null;
        foreach (var (member, value) in content)
        {
            if (Known.Contains(member)) known[member] = value?.DeepClone();
            else (unknown ??= new JsonObject())[member] = value?.DeepClone();
        }

        return new CodecReadResult(known, null, [], [], unknown);
    }

    public object ReadLocal(JsonObject content) => content.DeepClone();
    public JsonObject Write(object content) => (JsonObject) ((JsonObject) content).DeepClone();
    public string NameOf(object content) => ((JsonObject) content)["name"]!.GetValue<string>();
    public string? SubtypeOf(object content) => ((JsonObject) content)["type"]?.GetValue<string>();
    public int ChildCountOf(object content) => (((JsonObject) content)["children"] as JsonArray)?.Count ?? 0;

    public DataSyncPublishable Publish(object localContent, DataSyncOverlay overlay, bool childrenLocal)
    {
        var content = (JsonObject) ((JsonObject) localContent).DeepClone();
        if (content["tooBig"] is JsonValue big && big.GetValue<bool>())
            return new DataSyncPublishable(null, 0, [], DataSyncHeldReason.Invalid, "tooManyChildren");
        var children = content["children"] as JsonArray ?? [];
        var hidden = overlay.HiddenChildIds.ToHashSet();
        var kept = new JsonArray();
        var withheld = 0;
        foreach (var child in children.Select(c => (JsonObject) c!))
        {
            var id = child["id"]?.GetValue<string>() ?? "";
            var parent = child["parent"]?.GetValue<string>();
            if (hidden.Contains(id) || (parent is not null && hidden.Contains(parent)))
            {
                hidden.Add(id);
                withheld++;
                continue;
            }

            if (id.Length == 0 || (child["label"]?.GetValue<string>() ?? "").Length == 0)
            {
                withheld++;
                continue;
            }

            kept.Add(child.DeepClone());
        }

        if (childrenLocal)
        {
            withheld = children.Count;
            content.Remove("children");
            content["childrenLocal"] = true;
        }
        else
        {
            content["children"] = kept;
        }

        return new DataSyncPublishable(content, withheld, []);
    }

    public JsonObject ComparisonForm(object publishedContent, string? orderKey, bool childrenLocal)
    {
        var form = (JsonObject) ((JsonObject) publishedContent).DeepClone();
        if (form["children"] is JsonArray children)
        {
            var labels = children.Select(c => (JsonObject) c!).ToDictionary(c => c["id"]!.GetValue<string>(),
                c => c["label"]!.GetValue<string>());
            form["children"] = new JsonArray(children.Select(c => (JsonObject) c!)
                .Select(c => $"{(c["parent"]?.GetValue<string>() is { } p ? labels.GetValueOrDefault(p) + "/" : "")}{c["label"]!.GetValue<string>()}")
                .Distinct().OrderBy(l => l, StringComparer.Ordinal).Select(l => (JsonNode?) JsonValue.Create(l))
                .ToArray());
        }

        if (orderKey is not null) form["orderKey"] = orderKey;
        if (childrenLocal) form["childrenLocal"] = true;
        if (ComparisonFormVersion > 1) form["formVersion"] = ComparisonFormVersion;
        return form;
    }

    public IReadOnlyList<DataSyncChildInfo> ChildrenOf(object content) =>
        (((JsonObject) content)["children"] as JsonArray ?? []).Select(c => (JsonObject) c!)
        .Select(c => new DataSyncChildInfo(c["id"]!.GetValue<string>(), c["parent"]?.GetValue<string>(),
            new DataSyncDisplayValue(c["label"]!.GetValue<string>(), Group: c["group"]?.GetValue<string>())))
        .ToList();

    public DataSyncNaturalMatch MatchNatural(object incoming, object local) => throw new NotSupportedException();
    public EntityDiff Diff(object local, object incoming) => throw new NotSupportedException();

    public MergeResult Merge(object local, object incoming, IReadOnlySet<string> acceptedChangeIds) =>
        throw new NotSupportedException();

    public MergeResult PrepareCreate(object incoming, string? nameOverride) => throw new NotSupportedException();

    public IReadOnlyList<string> ChildDeletionCandidates(DataSyncChildCandidatesInput input) =>
        throw new NotSupportedException();

    public DataSyncMerge3Result Merge3(DataSyncMerge3Input input) => throw new NotSupportedException();
}

/// <summary>
/// A stand-in for the extension group codec (the pure engine's, package A): canonical content
/// <c>{"extensions":[…],"name"}</c>, extensions as children whose ids are the canonical strings, overlays by
/// extension, a comparison form equal to the published content.
/// </summary>
internal sealed class TestExtensionGroupCodec : IDataSyncKindCodec
{
    public DataSyncKindDescriptor Descriptor { get; } = new(DataSyncKindIds.ExtensionGroup, 1, [], typeof(JsonObject),
        true, false, true, false, "extension");

    public int ComparisonFormVersion => 1;

    public JsonObject Upgrade(JsonObject content, int fromSchemaVersion) => content;

    public CodecReadResult Read(JsonObject content, DataSyncLimits limits) =>
        content["name"] is JsonValue && content["extensions"] is JsonArray
            ? new CodecReadResult(content.DeepClone().AsObject(), null, [], [])
            : new CodecReadResult(null, DataSyncHeldReason.Invalid, ["shape"], []);

    public object ReadLocal(JsonObject content)
    {
        if (content["name"] is not JsonValue name || name.GetValueKind() != JsonValueKind.String ||
            content["extensions"] is not JsonArray)
            throw new InvalidOperationException("Not extension group content.");
        return content.DeepClone().AsObject();
    }

    public JsonObject Write(object content) => ((JsonObject) content).DeepClone().AsObject();
    public string NameOf(object content) => ((JsonObject) content)["name"]!.GetValue<string>();
    public string? SubtypeOf(object content) => null;
    public int ChildCountOf(object content) => ((JsonArray) ((JsonObject) content)["extensions"]!).Count;

    public DataSyncPublishable Publish(object localContent, DataSyncOverlay overlay, bool childrenLocal)
    {
        var content = ((JsonObject) localContent).DeepClone().AsObject();
        var hidden = overlay.HiddenChildIds.ToHashSet();
        var all = ((JsonArray) content["extensions"]!).Select(e => e!.GetValue<string>()).ToList();
        content["extensions"] = new JsonArray(all.Where(e => !hidden.Contains(e))
            .Select(e => (JsonNode?) JsonValue.Create(e)).ToArray());
        return new DataSyncPublishable(content, all.Count(hidden.Contains), []);
    }

    public JsonObject ComparisonForm(object publishedContent, string? orderKey, bool childrenLocal) =>
        ((JsonObject) publishedContent).DeepClone().AsObject();

    public IReadOnlyList<DataSyncChildInfo> ChildrenOf(object content) =>
        ((JsonArray) ((JsonObject) content)["extensions"]!).Select(e => e!.GetValue<string>())
        .Select(e => new DataSyncChildInfo(e, null, new DataSyncDisplayValue(e))).ToList();

    public DataSyncNaturalMatch MatchNatural(object incoming, object local) => throw new NotSupportedException();
    public EntityDiff Diff(object local, object incoming) => throw new NotSupportedException();

    public MergeResult Merge(object local, object incoming, IReadOnlySet<string> acceptedChangeIds) =>
        throw new NotSupportedException();

    public MergeResult PrepareCreate(object incoming, string? nameOverride) => throw new NotSupportedException();

    public IReadOnlyList<string> ChildDeletionCandidates(DataSyncChildCandidatesInput input) =>
        throw new NotSupportedException();

    public DataSyncMerge3Result Merge3(DataSyncMerge3Input input) => throw new NotSupportedException();
}

/// <summary>Move detection answered by the test (the algorithm is the pure engine's, package A); records its input.</summary>
internal sealed class FakeOrderMoveDetector : IDataSyncOrderMoveDetector
{
    public Func<IReadOnlyList<DataSyncOrderMoveEntry>, IReadOnlyDictionary<string, string>> Answer { get; set; } =
        _ => new Dictionary<string, string>();

    public List<IReadOnlyList<DataSyncOrderMoveEntry>> Calls { get; } = [];

    public IReadOnlyDictionary<string, string> DetectMoves(IReadOnlyList<DataSyncOrderMoveEntry> localOrder,
        int maxOrderKeyLength)
    {
        Calls.Add(localOrder);
        return Answer(localOrder);
    }

    /// <summary>Keys every entry without one "k{localKey}", like a planner appending new entities.</summary>
    public static IReadOnlyDictionary<string, string> KeyNewOnes(IReadOnlyList<DataSyncOrderMoveEntry> entries) =>
        entries.Where(e => e.OrderKey is null).ToDictionary(e => e.LocalKey, e => "k" + e.LocalKey);
}
