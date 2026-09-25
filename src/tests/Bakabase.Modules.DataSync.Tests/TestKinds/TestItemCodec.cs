using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Tests.TestKinds;

/// <summary>
/// A small generic kind for engine tests and the convergence simulator: a name (scalar, §8.5.2), a colour
/// (appearance, §8.5.5), an order key (the kind has order, §3.7), and children with ids and labels that merge
/// like options (§8.5.4) without label classes: a peer child maps to a local one through the base's child map,
/// then an equal id, then the first unmapped local child with an equal label.
/// </summary>
/// <remarks>
/// It follows the §3.4 rule on its own terms: the comparison form drops child ids and local child order (neither
/// is transferred) and keeps the name, the colour, the order key and the multiset of labels, which
/// <see cref="Merge3"/> all transfer. Deletions go by usage (holds when in use) and B4 uses
/// <see cref="DataSyncAutoApplyPolicy.Default"/>. Remapped ids are deterministic (<c>{id}~{n}</c>), so a simulator
/// run is reproducible.
/// </remarks>
public sealed class TestItemCodec : DataSyncKindCodec<TestItemContent>
{
    public const string Kind = "testItem";
    public const string ChildPathPrefix = "child:";

    public static TestItemCodec Instance { get; } = new();

    /// <param name="schemaVersion">
    /// The content schema this build writes; above 1, older content upgrades unchanged (a simulated upgrade of the
    /// build, §8.12).
    /// </param>
    public TestItemCodec(int schemaVersion = 1) =>
        Descriptor = new DataSyncKindDescriptor(Kind, schemaVersion, [], typeof(TestItemContent),
            AutoLinkIdentical: false, HasOrder: true, HasChildren: true, SupportsChildrenLocal: false, ChildNoun: "child");

    public override DataSyncKindDescriptor Descriptor { get; }

    public override JsonObject Upgrade(JsonObject content, int fromSchemaVersion) =>
        fromSchemaVersion < Descriptor.SchemaVersion ? content : base.Upgrade(content, fromSchemaVersion);

    public override int ComparisonFormVersion => 1;

    // ---- JSON ---------------------------------------------------------------------------------

    protected override CodecReadResult ReadCore(JsonObject content, DataSyncLimits limits)
    {
        var warnings = new List<DataSyncPlanWarning>();
        JsonObject? unknown = null;
        foreach (var (member, value) in content.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (member is "name" or "color" or "children" or "type") continue;
            (unknown ??= new JsonObject())[member] = value?.DeepClone();
        }

        if (unknown is not null)
            warnings.Add(Warning(DataSyncWarningCode.UnknownFieldsIgnored, ("count", unknown.Count.ToString(CultureInfo.InvariantCulture))));

        if (!TryGetString(content, "name", out var name) || name.Length == 0 || name.Length > limits.MaxNameLength ||
            !IsClean(name)) return Held("name", warnings, unknown);

        string? type = null;
        if (content.ContainsKey("type"))
        {
            if (!TryGetString(content, "type", out var t) || t.Length is 0 or > 64 || !IsClean(t))
                return Held("type", warnings, unknown);
            type = t;
        }

        string? color = null;
        if (content.ContainsKey("color"))
        {
            if (!TryGetString(content, "color", out var c)) return Held("color", warnings, unknown);
            color = c.Length <= limits.MaxColorLength ? c : null;
        }

        var children = new List<TestChild>();
        if (content.ContainsKey("children"))
        {
            if (content["children"] is not JsonArray array) return Held("children", warnings, unknown);
            if (array.Count > limits.MaxOptionsPerProperty) return Held("tooManyChildren", warnings, unknown);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in array)
            {
                if (node is JsonObject child && TryGetString(child, "id", out var id) &&
                    TryGetString(child, "label", out var label) && id.Length is > 0 && id.Length <= limits.MaxUuidLength &&
                    id != "*" && id.All(ch => !char.IsControl(ch)) && label.Length <= limits.MaxLabelLength &&
                    label.Length > 0 && IsClean(label))
                {
                    if (ids.Add(id))
                    {
                        children.Add(new TestChild(id, label));
                        continue;
                    }

                    warnings.Add(Warning(DataSyncWarningCode.OptionDropped, ("uuid", id), ("reason", "duplicateUuid"),
                        ("descendants", "0")));
                    continue;
                }

                warnings.Add(Warning(DataSyncWarningCode.OptionDropped, ("reason", "label"), ("descendants", "0")));
            }
        }

        return new CodecReadResult(new TestItemContent(name, color, children, type), null, [], warnings, unknown);
    }

    public override TestItemContent ReadLocal(JsonObject content)
    {
        var name = (string?)content["name"] ?? throw new InvalidOperationException("No name.");
        var color = (string?)content["color"];
        var type = (string?)content["type"];
        var children = content["children"] is JsonArray array
            ? array.Select(n => new TestChild((string)n!["id"]!, (string)n["label"]!))
            : [];
        return new TestItemContent(name, color, children, type);
    }

    public override JsonObject Write(TestItemContent content)
    {
        var json = new JsonObject { ["name"] = content.Name };
        if (content.Color is not null) json["color"] = content.Color;
        if (content.Type is not null) json["type"] = content.Type;
        if (content.Children.Count > 0)
        {
            json["children"] = new JsonArray(content.Children
                .Select(c => (JsonNode?)new JsonObject { ["id"] = c.Id, ["label"] = c.Label }).ToArray());
        }

        return json;
    }

    public override string NameOf(TestItemContent content) => content.Name;
    public override string? SubtypeOf(TestItemContent content) => content.Type;
    public override int ChildCountOf(TestItemContent content) => content.Children.Count;

    public override IReadOnlyList<DataSyncChildInfo> ChildrenOf(TestItemContent content) =>
        content.Children.Select(c => new DataSyncChildInfo(c.Id, null, new DataSyncDisplayValue(c.Label))).ToList();

    // ---- first contact (review) ---------------------------------------------------------------

    public override DataSyncNaturalMatch MatchNatural(TestItemContent incoming, TestItemContent local)
    {
        var sameName = string.Equals(incoming.Name.Trim(), local.Name.Trim(), StringComparison.OrdinalIgnoreCase);
        if (sameName && incoming.Type != local.Type) return DataSyncNaturalMatch.Clash;
        if (incoming.Name == local.Name)
        {
            return JsonNode.DeepEquals(ComparisonForm(incoming, null, false), ComparisonForm(local, null, false))
                ? DataSyncNaturalMatch.Identical
                : DataSyncNaturalMatch.Exact;
        }

        return string.Equals(incoming.Name.Trim(), local.Name.Trim(), StringComparison.OrdinalIgnoreCase)
            ? DataSyncNaturalMatch.Similar
            : DataSyncNaturalMatch.None;
    }

    public override EntityDiff Diff(TestItemContent local, TestItemContent incoming)
    {
        var changes = new List<DataSyncFieldChange>();
        if (local.Name != incoming.Name)
            changes.Add(new DataSyncFieldChange("name", DataSyncFieldChangeKind.Set, "name",
                new DataSyncDisplayValue(local.Name), new DataSyncDisplayValue(incoming.Name), null, null));
        if (incoming.Color is not null && incoming.Color != local.Color)
            changes.Add(new DataSyncFieldChange("color", DataSyncFieldChangeKind.Set, "color",
                new DataSyncDisplayValue(null, local.Color), new DataSyncDisplayValue(null, incoming.Color), null, null));

        var map = MapByIdThenLabel(incoming.Children, local.Children, new Dictionary<string, string>());
        var unchanged = 0;
        foreach (var child in incoming.Children)
        {
            if (!map.TryGetValue(child.Id, out var localId))
            {
                changes.Add(new DataSyncFieldChange("child:add:" + child.Id, DataSyncFieldChangeKind.AddChild,
                    "children", null, new DataSyncDisplayValue(child.Label), null, null));
                continue;
            }

            var localLabel = local.Children.First(c => c.Id == localId).Label;
            if (localLabel == child.Label) unchanged++;
            else
                changes.Add(new DataSyncFieldChange("child:rename:" + child.Id, DataSyncFieldChangeKind.RenameChild,
                    "children", new DataSyncDisplayValue(localLabel), new DataSyncDisplayValue(child.Label), null, null));
        }

        return new EntityDiff(changes, [], unchanged, local.Children.Count - map.Count);
    }

    public override MergeResult Merge(TestItemContent local, TestItemContent incoming,
        IReadOnlySet<string> acceptedChangeIds)
    {
        var map = MapByIdThenLabel(incoming.Children, local.Children, new Dictionary<string, string>());
        var children = local.Children.ToList();
        var added = new List<string>();
        var childMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var child in incoming.Children)
        {
            if (map.TryGetValue(child.Id, out var localId))
            {
                childMap[child.Id] = localId;
                if (acceptedChangeIds.Contains("child:rename:" + child.Id))
                    children[children.FindIndex(c => c.Id == localId)] = new TestChild(localId, child.Label);
            }
            else if (acceptedChangeIds.Contains("child:add:" + child.Id))
            {
                var id = FreeId(child.Id, children.Select(c => c.Id).ToHashSet(StringComparer.Ordinal));
                children.Add(new TestChild(id, child.Label));
                childMap[child.Id] = id;
                added.Add(id);
            }
        }

        var name = acceptedChangeIds.Contains("name") ? incoming.Name : local.Name;
        var color = acceptedChangeIds.Contains("color") ? incoming.Color ?? local.Color : local.Color;
        return new MergeResult(new TestItemContent(name, color, children), childMap, added, []);
    }

    public override MergeResult PrepareCreate(TestItemContent incoming, string? nameOverride) =>
        new(incoming.With(name: nameOverride), incoming.Children.ToDictionary(c => c.Id, c => c.Id),
            incoming.Children.Select(c => c.Id).ToList(), []);

    // ---- continuous sync ----------------------------------------------------------------------

    public override DataSyncPublishable Publish(TestItemContent localContent, DataSyncOverlay overlay, bool childrenLocal)
    {
        var hidden = overlay.HiddenChildIds.ToHashSet(StringComparer.Ordinal);
        var visible = localContent.Children.Where(c => !hidden.Contains(c.Id)).ToList();
        var withheld = localContent.Children.Count - visible.Count;
        var read = Read(Write(localContent.With(children: visible)), DataSyncLimits.Default);
        if (read.Held is { } held)
            return new DataSyncPublishable(null, withheld, read.Warnings.ToArray(), held, read.Errors.FirstOrDefault());
        var published = (TestItemContent)read.Content!;
        return new DataSyncPublishable(published, withheld + visible.Count - published.Children.Count,
            read.Warnings.ToArray());
    }

    public override JsonObject ComparisonForm(TestItemContent publishedContent, string? orderKey, bool childrenLocal)
    {
        var labels = publishedContent.Children.Select(c => c.Label).OrderBy(l => l, StringComparer.Ordinal);
        var form = new JsonObject { ["name"] = publishedContent.Name };
        if (publishedContent.Color is not null) form["color"] = publishedContent.Color;
        if (orderKey is not null) form["orderKey"] = orderKey;
        if (publishedContent.Type is not null) form["type"] = publishedContent.Type;
        form["children"] = new JsonArray(labels.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray());
        return form;
    }

    protected override IReadOnlyList<string> ChildDeletionCandidates(TestItemContent? baseContent, TestItemContent local,
        TestItemContent remote, DataSyncChildCandidatesInput input)
    {
        var plan = PlanChildren(baseContent, local, remote, input.LocalOverlay, input.Mode3, input.ChildMap,
            DataSyncLinkMode.TwoWay, true);
        return plan.Candidates;
    }

    protected override DataSyncMerge3Result Merge3(TestItemContent? baseContent, TestItemContent local,
        TestItemContent remote, DataSyncMerge3Input input)
    {
        var fields = new List<DataSyncFieldOutcome>();
        var warnings = new List<DataSyncPlanWarning>();
        var plan = PlanChildren(baseContent, local, remote, input.LocalOverlay, input.Mode3, input.ChildMap, input.Mode,
            input.LocalLastEditorIsSelf);

        // B4 (§8.5.4 step 7).
        var tripped = DataSyncBreakers.IsMassChildDeletion(plan.Candidates.Count, plan.VisibleLocalCount,
            DataSyncAutoApplyPolicy.Default);
        if (tripped && input.ChildDeletions == DataSyncChildDeletionMode.Normal)
        {
            return new DataSyncMerge3Result(local, [], new Dictionary<string, string>(input.ChildMap), [], [], [], [],
                plan.Candidates, [], false);
        }

        var name = MergeScalar("name", baseContent is not null, baseContent?.Name, local.Name, remote.Name, input, fields)!;
        // A peer's type change never reaches Merge3 (the merger freezes it, §8.5.6); a local one is kept, and
        // Convert takes the peer's.
        var type = input.Mode3 == DataSyncMerge3Mode.Convert
            ? remote.Type
            : MergeScalar("type", baseContent is not null, baseContent?.Type, local.Type, remote.Type, input, fields);
        var color = MergeColor(baseContent, local, remote, input, fields);

        var children = new List<TestChild>();
        var removed = new List<string>();
        var held = new List<string>();
        var labels = plan.Labels;
        foreach (var child in local.Children)
        {
            if (plan.Candidates.Contains(child.Id))
            {
                var usage = input.LocalChildUsage.TryGetValue(child.Id, out var count) ? count : int.MaxValue;
                var hold = input.ChildDeletions == DataSyncChildDeletionMode.ReviewEach || usage > 0;
                if (input.ChildDeletions == DataSyncChildDeletionMode.Restore)
                {
                    // Kept: not held, no item.
                }
                else if (hold)
                {
                    held.Add(child.Id);
                    fields.Add(new DataSyncFieldOutcome(ChildPathPrefix + plan.PeerIdOf(child.Id),
                        DataSyncFieldResolution.DeletionHeldInUse, null, new DataSyncDisplayValue(child.Label), null,
                        new DataSyncDisplayValue(child.Label)));
                }
                else
                {
                    removed.Add(child.Id);
                    fields.Add(new DataSyncFieldOutcome(ChildPathPrefix + plan.PeerIdOf(child.Id),
                        DataSyncFieldResolution.TookRemote, null, new DataSyncDisplayValue(child.Label), null, null));
                    continue;
                }
            }

            children.Add(new TestChild(child.Id, labels.TryGetValue(child.Id, out var label) ? label : child.Label));
        }

        foreach (var add in plan.Adds)
        {
            children.Add(add);
            if (plan.Restored.Contains(add.Id))
                warnings.Add(Warning(DataSyncWarningCode.ChildRestored, ("uuid", add.Id)));
            if (plan.RemappedFrom.TryGetValue(add.Id, out var from))
                warnings.Add(Warning(DataSyncWarningCode.OptionUuidRemapped, ("uuid", from), ("newUuid", add.Id)));
        }

        fields.AddRange(plan.Fields);
        var merged = new TestItemContent(name, color, children, type);
        return new DataSyncMerge3Result(merged, fields, plan.ChildMap, plan.Adds.Select(a => a.Id).ToList(), removed,
            held, plan.Released, [], warnings, false);
    }

    // ---- children -----------------------------------------------------------------------------

    private sealed record ChildPlan(
        Dictionary<string, string> ChildMap,         // peer id → local id, after the merge
        Dictionary<string, string> Labels,           // local id → merged label, where it changes
        List<TestChild> Adds,                        // appended after the local children
        HashSet<string> Restored,                    // adds that are edit-wins restorations
        Dictionary<string, string> RemappedFrom,     // added local id → the peer id it was remapped from
        List<string> Candidates,                     // local ids the peer deleted, unchanged here
        List<string> Released,
        List<DataSyncFieldOutcome> Fields,
        int VisibleLocalCount)
    {
        public string PeerIdOf(string localId) =>
            ChildMap.Where(p => p.Value == localId).Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal)
                .FirstOrDefault() ?? localId;
    }

    private static ChildPlan PlanChildren(TestItemContent? baseContent, TestItemContent local, TestItemContent remote,
        DataSyncOverlay overlay, DataSyncMerge3Mode mode3, IReadOnlyDictionary<string, string> childMap,
        DataSyncLinkMode mode, bool localLastEditorIsSelf)
    {
        var hidden = overlay.HiddenChildIds.ToHashSet(StringComparer.Ordinal);
        var visible = local.Children.Where(c => !hidden.Contains(c.Id)).ToList();
        var visibleById = visible.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var localIds = local.Children.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var baseById = (baseContent?.Children ?? []).GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var threeWay = baseContent is not null && mode3 is DataSyncMerge3Mode.ThreeWay;

        var map = MapByIdThenLabel(remote.Children, visible, childMap);
        var resultMap = new Dictionary<string, string>(map, StringComparer.Ordinal);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var adds = new List<TestChild>();
        var restored = new HashSet<string>(StringComparer.Ordinal);
        var remapped = new Dictionary<string, string>(StringComparer.Ordinal);
        var fields = new List<DataSyncFieldOutcome>();
        var taken = new HashSet<string>(localIds, StringComparer.Ordinal);

        foreach (var r in remote.Children)
        {
            var path = ChildPathPrefix + r.Id;
            if (map.TryGetValue(r.Id, out var localId))
            {
                var l = visibleById[localId].Label;
                if (l == r.Label) continue;
                var b = baseById.TryGetValue(r.Id, out var bc) ? bc.Label : null;
                DataSyncFieldResolution resolution;
                if (mode3 == DataSyncMerge3Mode.FastForward) resolution = DataSyncFieldResolution.TookRemote;
                else if (threeWay && b is not null)
                    resolution = l == b ? DataSyncFieldResolution.TookRemote
                        : r.Label == b ? DataSyncFieldResolution.KeptLocal
                        : Concurrent(mode, localLastEditorIsSelf);
                else resolution = Concurrent(mode, localLastEditorIsSelf);

                var result = resolution is DataSyncFieldResolution.TookRemote or DataSyncFieldResolution.FollowTookRemote
                    ? r.Label
                    : l;
                if (result != l) labels[localId] = result;
                fields.Add(new DataSyncFieldOutcome(path, resolution, b is null ? null : new DataSyncDisplayValue(b),
                    new DataSyncDisplayValue(l), new DataSyncDisplayValue(r.Label), new DataSyncDisplayValue(result)));
                continue;
            }

            // An overlay child is invisible to the merge: the peer's child stays mapped to it, and nothing is added.
            var hiddenCounterpart = childMap.TryGetValue(r.Id, out var hiddenMapped) && hidden.Contains(hiddenMapped)
                ? hiddenMapped
                : hidden.Contains(r.Id) && localIds.Contains(r.Id) ? r.Id : null;
            if (hiddenCounterpart is not null)
            {
                resultMap[r.Id] = hiddenCounterpart;
                continue;
            }

            // Not mapped: deleted here (three-way, in the base) or added by the peer.
            if (threeWay && baseById.TryGetValue(r.Id, out var inBase))
            {
                if (r.Label == inBase.Label) continue;   // this device's deletion stands
                var id = FreeId(r.Id, taken);
                adds.Add(new TestChild(id, r.Label));
                restored.Add(id);
                resultMap[r.Id] = id;
                fields.Add(new DataSyncFieldOutcome(path, DataSyncFieldResolution.EditWinsRestored,
                    new DataSyncDisplayValue(inBase.Label), null, new DataSyncDisplayValue(r.Label),
                    new DataSyncDisplayValue(r.Label)));
                continue;
            }

            var newId = FreeId(r.Id, taken);
            if (newId != r.Id) remapped[newId] = r.Id;
            adds.Add(new TestChild(newId, r.Label));
            resultMap[r.Id] = newId;
            fields.Add(new DataSyncFieldOutcome(path, DataSyncFieldResolution.TookRemote, null, null,
                new DataSyncDisplayValue(r.Label), new DataSyncDisplayValue(r.Label)));
        }

        // Deletions by the peer.
        var candidates = new List<string>();
        var mappedLocal = map.Values.ToHashSet(StringComparer.Ordinal);
        if (mode3 == DataSyncMerge3Mode.FastForward)
        {
            candidates.AddRange(visible.Where(c => !mappedLocal.Contains(c.Id)).Select(c => c.Id));
        }
        else if (threeWay)
        {
            var remoteIds = remote.Children.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var b in baseContent!.Children.Where(c => !remoteIds.Contains(c.Id)))
            {
                var counterpart = childMap.TryGetValue(b.Id, out var mapped) ? mapped : b.Id;
                if (mappedLocal.Contains(counterpart) || !visibleById.TryGetValue(counterpart, out var l)) continue;
                // Edit wins: a child renamed here since the base is kept and published again.
                if (l.Label == b.Label && !candidates.Contains(counterpart)) candidates.Add(counterpart);
            }
        }

        // A hold ends when the peer re-added the child since the base.
        var released = new List<string>();
        foreach (var hold in overlay.HeldChildren.Select(h => h.ChildId).Distinct(StringComparer.Ordinal))
        {
            var readded = remote.Children.Any(r =>
                (r.Id == hold || (childMap.TryGetValue(r.Id, out var m) && m == hold)) &&
                (baseContent is not null ? !baseById.ContainsKey(r.Id) : mode3 == DataSyncMerge3Mode.FastForward));
            if (readded && localIds.Contains(hold)) released.Add(hold);
        }

        return new ChildPlan(resultMap, labels, adds, restored, remapped, candidates, released, fields, visible.Count);
    }

    /// <summary>
    /// Peer child → local child: the base's child map, then an equal id, then the first unmapped local child with
    /// an equal label. Each local child is mapped once.
    /// </summary>
    private static Dictionary<string, string> MapByIdThenLabel(IReadOnlyList<TestChild> remote,
        IReadOnlyList<TestChild> local, IReadOnlyDictionary<string, string> childMap)
    {
        var localIds = local.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new List<TestChild>();
        foreach (var r in remote)
        {
            var candidate = childMap.TryGetValue(r.Id, out var mapped) && localIds.Contains(mapped) ? mapped
                : localIds.Contains(r.Id) ? r.Id
                : null;
            if (candidate is not null && used.Add(candidate)) map[r.Id] = candidate;
            else pending.Add(r);
        }

        foreach (var r in pending)
        {
            var byLabel = local.FirstOrDefault(c => c.Label == r.Label && !used.Contains(c.Id));
            if (byLabel is null) continue;
            used.Add(byLabel.Id);
            map[r.Id] = byLabel.Id;
        }

        return map;
    }

    private static string? MergeScalar(string path, bool hasBase, string? b, string? l, string? r, DataSyncMerge3Input input,
        List<DataSyncFieldOutcome> fields)
    {
        if (l == r) return l;
        DataSyncFieldResolution resolution;
        if (input.Mode3 is DataSyncMerge3Mode.FastForward) resolution = DataSyncFieldResolution.TookRemote;
        else if (hasBase && input.Mode3 is DataSyncMerge3Mode.ThreeWay or DataSyncMerge3Mode.Convert)
            resolution = l == b ? DataSyncFieldResolution.TookRemote
                : r == b ? DataSyncFieldResolution.KeptLocal
                : Concurrent(input.Mode, input.LocalLastEditorIsSelf);
        else resolution = Concurrent(input.Mode, input.LocalLastEditorIsSelf);
        var result = resolution is DataSyncFieldResolution.TookRemote or DataSyncFieldResolution.FollowTookRemote ? r : l;
        fields.Add(new DataSyncFieldOutcome(path, resolution, b is null ? null : new DataSyncDisplayValue(b),
            new DataSyncDisplayValue(l), new DataSyncDisplayValue(r), new DataSyncDisplayValue(result)));
        return result;
    }

    /// <summary>§8.5.5: one side changed → taken; both → the appearance winner; NoBase never clears.</summary>
    private static string? MergeColor(TestItemContent? baseContent, TestItemContent local, TestItemContent remote,
        DataSyncMerge3Input input, List<DataSyncFieldOutcome> fields)
    {
        string? b = baseContent?.Color, l = local.Color, r = remote.Color;
        if (l == r) return l;
        bool takeRemote;
        switch (input.Mode3)
        {
            case DataSyncMerge3Mode.FastForward:
            case DataSyncMerge3Mode.Convert:
                takeRemote = true;
                break;
            case DataSyncMerge3Mode.ThreeWay:
                takeRemote = l == b || (r != b && input.AppearanceWinner == DataSyncMergeSide.Remote);
                break;
            default:
                takeRemote = l is null || (r is not null && input.AppearanceWinner == DataSyncMergeSide.Remote);
                break;
        }

        var result = takeRemote ? r : l;
        fields.Add(new DataSyncFieldOutcome("color",
            takeRemote ? DataSyncFieldResolution.AppearanceTookRemote : DataSyncFieldResolution.AppearanceKeptLocal,
            new DataSyncDisplayValue(null, b), new DataSyncDisplayValue(null, l), new DataSyncDisplayValue(null, r),
            new DataSyncDisplayValue(null, result)));
        return result;
    }

    private static DataSyncFieldResolution Concurrent(DataSyncLinkMode mode, bool localLastEditorIsSelf) =>
        mode == DataSyncLinkMode.Follow && localLastEditorIsSelf
            ? DataSyncFieldResolution.FollowTookRemote
            : DataSyncFieldResolution.Conflict;

    /// <summary><paramref name="id"/>, or <c>{id}~{n}</c> with the smallest free n; the result is taken.</summary>
    private static string FreeId(string id, HashSet<string> taken)
    {
        var candidate = id;
        for (var n = 1; !taken.Add(candidate); n++) candidate = id + "~" + n.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    private static CodecReadResult Held(string error, List<DataSyncPlanWarning> warnings, JsonObject? unknown) =>
        new(null, DataSyncHeldReason.Invalid, [error], warnings, unknown);

    private static DataSyncPlanWarning Warning(DataSyncWarningCode code, params (string Key, string Value)[] args) =>
        new(code, null, args.ToDictionary(a => a.Key, a => a.Value));

    private static bool TryGetString(JsonObject json, string member, out string value)
    {
        value = null!;
        if (json[member] is not JsonValue v || v.GetValueKind() != JsonValueKind.String ||
            !v.TryGetValue(out string? s)) return false;
        value = s;
        return true;
    }

    private static bool IsClean(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\0') return false;
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) i++;
            else if (char.IsSurrogate(value[i])) return false;
        }

        return true;
    }
}
