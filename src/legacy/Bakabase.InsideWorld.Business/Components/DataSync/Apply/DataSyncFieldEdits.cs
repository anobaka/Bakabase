using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>
/// One conflicting path set to the peer's value or to a typed one (§9.2 <c>UseRemote</c>, <c>UseCustom</c>), on
/// canonical local JSON. Paths follow §8.5.1: scalars by name (<c>name</c>, <c>ignoreCase</c>,
/// <c>settings.precision</c>, <c>defaultValue</c>), a child class key as <c>{noun}:{peerId}</c> and its parent as
/// <c>{noun}:{peerId}:parent</c>. A peer child maps to a local one through the base's child map, else by an equal
/// id. <c>childrenLocal</c> is the side row's and is set by the caller; appearance and unknown members are never
/// items.
/// </summary>
internal static class DataSyncFieldEdits
{
    private const string DefaultValuePath = "defaultValue";
    private const string ParentSuffix = ":parent";
    private const string ColorSuffix = ":color";
    private const string UnknownPrefix = "x:";

    /// <summary>Writes the peer's value at <paramref name="path"/>; false when the path cannot be written here.</summary>
    public static bool TryUseRemote(JsonObject local, JsonObject remote, string path,
        IReadOnlyDictionary<string, string> childMap)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        if (path.StartsWith(UnknownPrefix, StringComparison.Ordinal) || path.EndsWith(ColorSuffix, StringComparison.Ordinal))
            return false;
        var separator = path.IndexOf(':');
        if (separator < 0)
        {
            var value = DataSyncContentNodes.GetPath(remote, path)?.DeepClone();
            if (path == DefaultValuePath && value is not null) value = MapRefs(value, childMap);
            DataSyncContentNodes.SetPath(local, path, value);
            return true;
        }

        var parent = path.EndsWith(ParentSuffix, StringComparison.Ordinal);
        var peerId = parent ? path[(separator + 1)..^ParentSuffix.Length] : path[(separator + 1)..];
        var remoteNodes = DataSyncContentNodes.Index(remote);
        var localNodes = DataSyncContentNodes.Index(local);
        if (!remoteNodes.TryGetValue(peerId, out var theirs) ||
            !localNodes.TryGetValue(LocalIdOf(peerId, childMap), out var ours) ||
            theirs.Node is not JsonObject theirObject || ours.Node is not JsonObject ourObject) return false;

        if (!parent)
        {
            DataSyncContentNodes.CopyLabel(theirObject, ourObject);
            return true;
        }

        var remoteParent = DataSyncContentNodes.ParentOf(theirs);
        var localParent = remoteParent is null ? null : LocalIdOf(remoteParent, childMap);
        if (DataSyncContentNodes.IsFlat(ours))
        {
            if (localParent is null) ourObject.Remove(DataSyncContentNodes.ParentMember);
            else ourObject[DataSyncContentNodes.ParentMember] = localParent;
            return true;
        }

        if (string.Equals(ours.ParentId, localParent, StringComparison.Ordinal)) return true;
        var target = localParent is null ? null : localNodes.GetValueOrDefault(localParent);
        if (localParent is not null && target is null) return false;
        // A move never puts a node under itself or below it (§8.5.4: a cycle keeps the local parent).
        for (var ancestor = target; ancestor is not null; ancestor = ancestor.ParentId is null ? null : localNodes.GetValueOrDefault(ancestor.ParentId))
        {
            if (ancestor.Id == ours.Id) return false;
        }

        return DataSyncContentNodes.Move(local, ours, target, theirs.ContainerName, int.MaxValue);
    }

    /// <summary>Writes a typed value: an entity name, or a child's label (§9.2 <c>UseCustom</c>).</summary>
    public static bool TryUseCustom(JsonObject local, string path, string value, IReadOnlyDictionary<string, string> childMap)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(value);
        if (path.EndsWith(ParentSuffix, StringComparison.Ordinal) || path.EndsWith(ColorSuffix, StringComparison.Ordinal) ||
            path.StartsWith(UnknownPrefix, StringComparison.Ordinal)) return false;
        var separator = path.IndexOf(':');
        if (separator < 0)
        {
            if (DataSyncContentNodes.GetPath(local, path) is not JsonValue current ||
                current.GetValueKind() != JsonValueKind.String) return false;
            DataSyncContentNodes.SetPath(local, path, JsonValue.Create(value));
            return true;
        }

        var peerId = path[(separator + 1)..];
        if (!DataSyncContentNodes.Index(local).TryGetValue(LocalIdOf(peerId, childMap), out var ours) ||
            ours.Node is not JsonObject ourObject) return false;
        DataSyncContentNodes.SetLabel(ourObject, value);
        return true;
    }

    private static string LocalIdOf(string peerId, IReadOnlyDictionary<string, string> childMap) =>
        childMap.TryGetValue(peerId, out var local) ? local : peerId;

    /// <summary>A peer's references (<c>defaultValue</c>) translated to this device's child ids.</summary>
    private static JsonNode MapRefs(JsonNode node, IReadOnlyDictionary<string, string> childMap)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var element in array.ToList())
                {
                    if (element is not null) MapRefs(element, childMap);
                }

                break;
            case JsonObject o:
                foreach (var member in new[] { "uuid", "id" })
                {
                    if (o[member] is JsonValue v && v.GetValueKind() == JsonValueKind.String &&
                        childMap.TryGetValue(v.GetValue<string>(), out var mapped))
                        o[member] = mapped;
                }

                foreach (var (_, value) in o.ToList())
                {
                    if (value is JsonArray or JsonObject) MapRefs(value, childMap);
                }

                break;
        }

        return node;
    }
}
