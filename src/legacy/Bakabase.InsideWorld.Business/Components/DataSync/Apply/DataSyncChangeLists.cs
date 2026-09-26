using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using ModuleChangeList = Bakabase.Modules.DataSync.Merging.DataSyncEntityChangeList;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>What <see cref="DataSyncChangeLists.Apply"/> made of the current content.</summary>
/// <param name="Content">The edited canonical local JSON (not yet read back through the codec).</param>
/// <param name="AddedChildIds">Children written back (an update's <c>AddedChildIds</c>).</param>
/// <param name="RemovedChildIds">Children taken out, with everything below them (<c>RemovedChildIds</c>).</param>
/// <param name="Conflicts">Paths that changed since, so they cannot be written: undo refuses (<c>ChangedSinceImport</c>).</param>
/// <param name="ChildrenLocal">The side row's <c>childrenLocal</c> to set; null when it does not change.</param>
internal sealed record DataSyncChangeApplication(JsonObject Content, IReadOnlyList<string> AddedChildIds,
    IReadOnlyList<string> RemovedChildIds, IReadOnlyList<string> Conflicts, bool? ChildrenLocal)
{
    public bool Changed => AddedChildIds.Count > 0 || RemovedChildIds.Count > 0 || ChildrenLocal is not null || Edited;

    internal bool Edited { get; init; }
}

/// <summary>
/// The per-entity change list of an apply (§6.5, §8.11 pre-images version 2): scalar paths with their values before
/// and after, and children by LOCAL id with their own JSON and place, so undo can write back exactly the changed
/// paths and "Put the synced change back" exactly the undone ones — never the whole row.
/// </summary>
internal static class DataSyncChangeLists
{
    public const string ChildPathPrefix = "child:";
    public const string ChildrenLocalPath = "childrenLocal";

    /// <summary>The changes from <paramref name="before"/> to <paramref name="after"/> (both <c>ReadLocal</c> content).</summary>
    public static DataSyncEntityChanges Between(IDataSyncKindCodec codec, string localKey, object before,
        bool childrenLocalBefore, object after, bool childrenLocalAfter)
    {
        ArgumentNullException.ThrowIfNull(codec);
        var beforeJson = codec.Write(before);
        var afterJson = codec.Write(after);

        var beforeScalars = ModuleChangeList.ScalarsOf(beforeJson);
        var afterScalars = ModuleChangeList.ScalarsOf(afterJson);
        var scalars = beforeScalars.Keys.Union(afterScalars.Keys).Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new DataSyncScalarChange(p, beforeScalars.GetValueOrDefault(p)?.DeepClone(),
                afterScalars.GetValueOrDefault(p)?.DeepClone()))
            .Where(c => !JsonNode.DeepEquals(c.Before, c.After))
            .ToList();
        if (childrenLocalBefore != childrenLocalAfter)
        {
            scalars.Add(new DataSyncScalarChange(ChildrenLocalPath, JsonValue.Create(childrenLocalBefore),
                JsonValue.Create(childrenLocalAfter)));
        }

        var infosBefore = InfosOf(codec, before);
        var infosAfter = InfosOf(codec, after);
        var nodesBefore = DataSyncContentNodes.Index(beforeJson);
        var nodesAfter = DataSyncContentNodes.Index(afterJson);
        var children = new List<DataSyncChildChange>();
        foreach (var id in infosBefore.Keys.Concat(infosAfter.Keys.Where(k => !infosBefore.ContainsKey(k))))
        {
            var b = infosBefore.GetValueOrDefault(id);
            var a = infosAfter.GetValueOrDefault(id);
            var nb = nodesBefore.GetValueOrDefault(id);
            var na = nodesAfter.GetValueOrDefault(id);
            var ownBefore = nb is null ? null : DataSyncContentNodes.OwnContent(nb);
            var ownAfter = na is null ? null : DataSyncContentNodes.OwnContent(na);
            if (b is not null && a is not null && SameInfo(b, a) && JsonNode.DeepEquals(ownBefore, ownAfter)) continue;
            children.Add(new DataSyncChildChange(ChildPathPrefix + id, b, a, ownBefore, ownAfter, nb?.ContainerName,
                nb?.Index, na?.ContainerName, na?.Index));
        }

        return new DataSyncEntityChanges(codec.Descriptor.Kind, localKey, scalars, children);
    }

    /// <summary>
    /// Writes <paramref name="changes"/> onto the current content: backward (undo: the after-state becomes the
    /// before-state) or forward (the before-state becomes the after-state again). A path is written only while it
    /// still holds the value the change left there; a path already at the target is left alone; any other path
    /// changed since and is a conflict. Children are matched by id. A child is removed with everything below it only
    /// when the same list removes all of that too: one with a child added or moved under it since is a conflict.
    /// </summary>
    public static DataSyncChangeApplication Apply(IDataSyncKindCodec codec, object current, bool currentChildrenLocal,
        IReadOnlyList<DataSyncScalarChange> scalars, IReadOnlyList<DataSyncChildChange> children, bool backward)
    {
        ArgumentNullException.ThrowIfNull(codec);
        var json = codec.Write(current);
        var conflicts = new List<string>();
        var added = new List<string>();
        var removed = new List<string>();
        bool? childrenLocal = null;
        var edited = false;

        foreach (var s in scalars)
        {
            var from = backward ? s.After : s.Before;
            var to = backward ? s.Before : s.After;
            if (s.Path == ChildrenLocalPath)
            {
                var now = JsonValue.Create(currentChildrenLocal);
                if (JsonNode.DeepEquals(now, to)) continue;
                if (JsonNode.DeepEquals(now, from ?? JsonValue.Create(false))) childrenLocal = to?.GetValue<bool>() ?? false;
                else conflicts.Add(s.Path);
                continue;
            }

            var value = DataSyncContentNodes.GetPath(json, s.Path);
            if (JsonNode.DeepEquals(value, to)) continue;
            if (!JsonNode.DeepEquals(value, from))
            {
                conflicts.Add(s.Path);
                continue;
            }

            DataSyncContentNodes.SetPath(json, s.Path, to);
            edited = true;
        }

        var infos = InfosOf(codec, current);
        var nodes = DataSyncContentNodes.Index(json);
        var inserts = new List<(DataSyncChildChange Change, DataSyncChildInfo To, JsonObject Own, string Container, int Index)>();

        // Changed children first, then removals, then insertions (a parent is inserted before its children).
        foreach (var c in children.Where(c => c.Before is not null && c.After is not null))
        {
            var fromInfo = backward ? c.After! : c.Before!;
            var toInfo = backward ? c.Before! : c.After!;
            var toOwn = backward ? c.BeforeContent : c.AfterContent;
            var now = infos.GetValueOrDefault(c.ChildId);
            if (now is not null && SameInfo(now, toInfo) &&
                (nodes.GetValueOrDefault(c.ChildId) is not { } atTarget || toOwn is null ||
                 JsonNode.DeepEquals(DataSyncContentNodes.OwnContent(atTarget), toOwn))) continue;
            if (now is null || !SameInfo(now, fromInfo) || toOwn is null ||
                nodes.GetValueOrDefault(c.ChildId) is not { } node)
            {
                conflicts.Add(c.Path);
                continue;
            }

            DataSyncContentNodes.ReplaceOwn(node, toOwn);
            edited = true;
            if (!DataSyncContentNodes.IsFlat(node) && !string.Equals(node.ParentId, toInfo.ParentId, StringComparison.Ordinal))
            {
                var parent = toInfo.ParentId is null ? null : nodes.GetValueOrDefault(toInfo.ParentId);
                var container = (backward ? c.BeforeContainer : c.AfterContainer) ?? node.ContainerName;
                var index = (backward ? c.BeforeIndex : c.AfterIndex) ?? int.MaxValue;
                if ((toInfo.ParentId is not null && parent is null) ||
                    !DataSyncContentNodes.Move(json, node, parent, container, index))
                {
                    conflicts.Add(c.Path + ":parent");
                    continue;
                }

                nodes = DataSyncContentNodes.Index(json);
            }
        }

        // Removals, all checked before any is taken out. A child goes with everything below it, so it is removed only
        // while everything below it now is removed by this same list: a child added under it or moved under it since
        // is not this list's to take away (§8.11 restores exactly the changed paths), and the path is a conflict.
        var removals = new Dictionary<string, DataSyncChildChange>(StringComparer.Ordinal);
        foreach (var c in children.Where(c => (backward ? c.Before : c.After) is null))
        {
            var fromInfo = backward ? c.After : c.Before;
            if (fromInfo is null) continue;
            var now = infos.GetValueOrDefault(c.ChildId);
            if (now is null) continue;
            if (!SameInfo(now, fromInfo) || !nodes.ContainsKey(c.ChildId))
            {
                conflicts.Add(c.Path);
                continue;
            }

            removals.TryAdd(c.ChildId, c);
        }

        if (removals.Count > 0)
        {
            var below = ChildrenByParent(nodes);
            bool dropped;
            do
            {
                dropped = false;
                foreach (var (id, c) in removals.ToList())
                {
                    if (!HoldsChildWithoutId(nodes[id].Node) && SubtreeOf(below, id).All(removals.ContainsKey)) continue;
                    removals.Remove(id);
                    conflicts.Add(c.Path);
                    dropped = true;
                }
            } while (dropped);

            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in removals.Keys)
            {
                foreach (var x in SubtreeOf(below, id))
                {
                    if (taken.Add(x)) removed.Add(x);
                }

                // A nested child already went with its parent; removing it from the detached array changes nothing.
                DataSyncContentNodes.Remove(nodes[id]);
            }

            nodes = DataSyncContentNodes.Index(json);
        }

        foreach (var c in children.Where(c => (backward ? c.After : c.Before) is null))
        {
            var toInfo = backward ? c.Before : c.After;
            var own = backward ? c.BeforeContent : c.AfterContent;
            if (toInfo is null) continue;
            var now = infos.GetValueOrDefault(c.ChildId);
            if (now is not null)
            {
                if (!SameInfo(now, toInfo)) conflicts.Add(c.Path);
                continue;
            }

            var container = backward ? c.BeforeContainer : c.AfterContainer;
            if (own is null || container is null)
            {
                conflicts.Add(c.Path);
                continue;
            }

            inserts.Add((c, toInfo, own, container, (backward ? c.BeforeIndex : c.AfterIndex) ?? int.MaxValue));
        }

        // A child whose parent is inserted too waits for it.
        while (inserts.Count > 0)
        {
            var progress = false;
            foreach (var insert in inserts.OrderBy(i => i.Index).ToList())
            {
                var parentId = insert.To.ParentId;
                var flat = insert.Own.ContainsKey(DataSyncContentNodes.ParentMember);
                DataSyncContentNode? parent = null;
                if (parentId is not null && !flat)
                {
                    parent = nodes.GetValueOrDefault(parentId);
                    if (parent is null) continue;
                }

                if (DataSyncContentNodes.Insert(json, parent, insert.Own, insert.Container, insert.Index) is null)
                {
                    conflicts.Add(insert.Change.Path);
                }
                else
                {
                    added.Add(insert.Change.ChildId);
                    nodes = DataSyncContentNodes.Index(json);
                }

                inserts.Remove(insert);
                progress = true;
            }

            if (progress) continue;
            conflicts.AddRange(inserts.Select(i => i.Change.Path));
            break;
        }

        return new DataSyncChangeApplication(json, added, removed, conflicts, childrenLocal) { Edited = edited };
    }

    /// <summary>
    /// The changes of <paramref name="applied"/> that <paramref name="current"/> undoes (§6.5): a scalar back at its
    /// before value, an added child gone, a changed child back at its before-state, a removed child back.
    /// </summary>
    public static DataSyncEntityChanges Undone(IDataSyncKindCodec codec, object current, bool currentChildrenLocal,
        DataSyncEntityChanges applied)
    {
        var json = codec.Write(current);
        var infos = InfosOf(codec, current);
        var scalars = applied.Scalars.Where(s =>
        {
            if (JsonNode.DeepEquals(s.Before, s.After)) return false;
            var now = s.Path == ChildrenLocalPath
                ? JsonValue.Create(currentChildrenLocal)
                : DataSyncContentNodes.GetPath(json, s.Path);
            var before = s.Path == ChildrenLocalPath ? s.Before ?? JsonValue.Create(false) : s.Before;
            return JsonNode.DeepEquals(now, before);
        }).ToList();
        var children = applied.Children.Where(c =>
        {
            var now = infos.GetValueOrDefault(c.ChildId);
            return (c.Before, c.After) switch
            {
                (null, not null) => now is null,
                (not null, null) => now is not null,
                ({ } before, { } after) => now is not null && !SameInfo(before, after) && SameInfo(now, before),
                _ => false,
            };
        }).ToList();
        return applied with { Scalars = scalars, Children = children };
    }

    private static Dictionary<string, DataSyncChildInfo> InfosOf(IDataSyncKindCodec codec, object content)
    {
        var result = new Dictionary<string, DataSyncChildInfo>(StringComparer.Ordinal);
        foreach (var child in codec.ChildrenOf(content))
        {
            if (!string.IsNullOrEmpty(child.Id)) result.TryAdd(child.Id, child);
        }

        return result;
    }

    private static bool SameInfo(DataSyncChildInfo a, DataSyncChildInfo b) =>
        string.Equals(a.Display.Text, b.Display.Text, StringComparison.Ordinal) &&
        string.Equals(a.Display.Group, b.Display.Group, StringComparison.Ordinal) &&
        string.Equals(a.Display.Color, b.Display.Color, StringComparison.Ordinal) &&
        string.Equals(a.ParentId, b.ParentId, StringComparison.Ordinal);

    /// <summary>Each child's children as the content holds them now: nested, or by a flat <c>parent</c> member.</summary>
    private static Dictionary<string, List<string>> ChildrenByParent(IReadOnlyDictionary<string, DataSyncContentNode> nodes)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in nodes.Values)
        {
            if (DataSyncContentNodes.ParentOf(node) is not { } parent) continue;
            if (!result.TryGetValue(parent, out var list)) result[parent] = list = [];
            list.Add(node.Id);
        }

        return result;
    }

    /// <summary>A child and every child below it, parents first.</summary>
    private static List<string> SubtreeOf(IReadOnlyDictionary<string, List<string>> below, string id)
    {
        var result = new List<string> { id };
        var seen = new HashSet<string>(StringComparer.Ordinal) { id };
        for (var i = 0; i < result.Count; i++)
        {
            foreach (var child in below.GetValueOrDefault(result[i]) ?? [])
            {
                if (seen.Add(child)) result.Add(child);
            }
        }

        return result;
    }

    /// <summary>A child holds a child without an id: nothing can say whether a change list added it.</summary>
    private static bool HoldsChildWithoutId(JsonNode node) =>
        node is JsonObject o && o.Any(m => m.Value is JsonArray array && array.Any(e =>
            e is JsonObject child && (DataSyncContentNodes.IdOf(child) is null || HoldsChildWithoutId(child))));
}
