using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>One child of an entity where it sits in the entity's canonical local JSON.</summary>
/// <param name="Id">The child's id: its <c>id</c> or <c>uuid</c> member, or the value itself for a bare value.</param>
/// <param name="Node">The child's JSON: an object, or a string value (an extension).</param>
/// <param name="Container">The array that holds it.</param>
/// <param name="ContainerName">The member that array is (<c>choices</c>, <c>children</c>, <c>extensions</c>…).</param>
/// <param name="Owner">The object whose member the array is: the content itself, or the parent child.</param>
/// <param name="ParentId">The id of the child that holds it (nested children); null at the top level.</param>
internal sealed record DataSyncContentNode(string Id, JsonNode Node, JsonArray Container, string ContainerName,
    JsonObject Owner, string? ParentId)
{
    public int Index => Container.IndexOf(Node);
}

/// <summary>
/// Children of canonical local content, found and edited by id without a kind's own types: undo writes back exactly
/// the changed paths (§8.11), "Put the synced change back" writes the undone ones again (§6.5), and a resolution sets
/// one path to the peer's value (§9.2). Every kind's canonical content keeps its children in arrays of objects with an
/// <c>id</c> or <c>uuid</c> member (nested ones under their parent), or as arrays of bare strings at the top level
/// (extensions), and its scalars as members of the content or of a nested object (<c>settings.precision</c>).
/// <c>defaultValue</c> holds references, never children. The result of every edit is read back through the codec
/// (<c>ReadLocal</c>, then <c>Write</c>), which rejects a shape the kind does not have.
/// </summary>
internal static class DataSyncContentNodes
{
    /// <summary>The own content of a child that is a bare value: <c>{"value": "…"}</c>.</summary>
    public const string ValueMember = "value";

    public const string ParentMember = "parent";
    private const string ColorMember = "color";
    private const string DefaultValueMember = "defaultValue";
    private static readonly string[] IdMembers = ["id", "uuid"];

    /// <summary>Every child of <paramref name="content"/> by id (the first one wins for a repeated id).</summary>
    public static Dictionary<string, DataSyncContentNode> Index(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var result = new Dictionary<string, DataSyncContentNode>(StringComparer.Ordinal);
        Walk(content, null, result);
        return result;
    }

    private static void Walk(JsonObject owner, string? ownerId, Dictionary<string, DataSyncContentNode> result)
    {
        foreach (var (name, value) in owner.ToList())
        {
            if (value is not JsonArray array || (ownerId is null && name == DefaultValueMember)) continue;
            foreach (var element in array)
            {
                switch (element)
                {
                    case JsonObject child when IdOf(child) is { } id:
                        result.TryAdd(id, new DataSyncContentNode(id, child, array, name, owner, ownerId));
                        Walk(child, id, result);
                        break;
                    case JsonValue v when ownerId is null && v.GetValueKind() == JsonValueKind.String &&
                                          v.GetValue<string>() is { Length: > 0 } text:
                        result.TryAdd(text, new DataSyncContentNode(text, v, array, name, owner, null));
                        break;
                }
            }
        }
    }

    public static string? IdOf(JsonObject child)
    {
        foreach (var member in IdMembers)
        {
            if (child[member] is JsonValue v && v.GetValueKind() == JsonValueKind.String &&
                v.GetValue<string>() is { Length: > 0 } id) return id;
        }

        return null;
    }

    /// <summary>A child's own JSON without the arrays it holds (its children); a bare value as <c>{"value": …}</c>.</summary>
    public static JsonObject OwnContent(DataSyncContentNode node)
    {
        if (node.Node is not JsonObject o) return new JsonObject { [ValueMember] = node.Node.DeepClone() };
        var own = new JsonObject();
        foreach (var (name, value) in o)
        {
            if (value is JsonArray) continue;
            own[name] = value?.DeepClone();
        }

        return own;
    }

    /// <summary>The child's parent as its JSON says: its structural parent, or a flat <c>parent</c> member.</summary>
    public static string? ParentOf(DataSyncContentNode node) =>
        node.ParentId ?? (node.Node is JsonObject o && o[ParentMember] is JsonValue p &&
                          p.GetValueKind() == JsonValueKind.String ? p.GetValue<string>() : null);

    /// <summary>A child is flat when its parent is a member of its own JSON rather than where it sits.</summary>
    public static bool IsFlat(DataSyncContentNode node) => node.Node is JsonObject o && o.ContainsKey(ParentMember);

    /// <summary>Removes a child and everything below it from its container.</summary>
    public static void Remove(DataSyncContentNode node) => node.Container.Remove(node.Node);

    /// <summary>
    /// Inserts a child from its own JSON under <paramref name="parent"/> (null: the top level) in
    /// <paramref name="containerName"/> at <paramref name="index"/> (clamped). Returns the inserted node, or null when
    /// the parent cannot hold children.
    /// </summary>
    public static JsonNode? Insert(JsonObject content, DataSyncContentNode? parent, JsonObject own, string containerName,
        int index)
    {
        JsonObject owner;
        if (parent is null) owner = content;
        else if (parent.Node is JsonObject parentObject) owner = parentObject;
        else return null;

        if (owner[containerName] is not JsonArray container)
        {
            if (owner.ContainsKey(containerName)) return null;
            owner[containerName] = container = new JsonArray();
        }

        JsonNode node = own.Count == 1 && own[ValueMember] is JsonValue bare
            ? bare.DeepClone()
            : (JsonObject) own.DeepClone();
        container.Insert(Math.Clamp(index, 0, container.Count), node);
        return node;
    }

    /// <summary>Replaces a child's own members with <paramref name="own"/>'s, keeping the arrays it holds.</summary>
    public static bool ReplaceOwn(DataSyncContentNode node, JsonObject own)
    {
        if (node.Node is not JsonObject o) return own[ValueMember] is JsonValue v && JsonNode.DeepEquals(v, node.Node);
        foreach (var name in o.Where(m => m.Value is not JsonArray).Select(m => m.Key).ToList()) o.Remove(name);
        foreach (var (name, value) in own)
        {
            if (value is JsonArray) continue;
            o[name] = value?.DeepClone();
        }

        return true;
    }

    /// <summary>
    /// Moves a nested child (with everything below it) under <paramref name="parent"/> (null: the top level) in
    /// <paramref name="containerName"/> at <paramref name="index"/>.
    /// </summary>
    public static bool Move(JsonObject content, DataSyncContentNode node, DataSyncContentNode? parent,
        string containerName, int index)
    {
        JsonObject owner;
        if (parent is null) owner = content;
        else if (parent.Node is JsonObject parentObject) owner = parentObject;
        else return false;
        if (owner[containerName] is not JsonArray container)
        {
            if (owner.ContainsKey(containerName)) return false;
            owner[containerName] = container = new JsonArray();
        }

        node.Container.Remove(node.Node);
        container.Insert(Math.Clamp(index, 0, container.Count), node.Node);
        return true;
    }

    /// <summary>A child's label members: its string members that are neither its id, its colour nor its parent.</summary>
    public static IEnumerable<string> LabelMembers(JsonObject child) =>
        child.Where(m => m.Value is JsonValue v && v.GetValueKind() == JsonValueKind.String &&
                         !IdMembers.Contains(m.Key) && m.Key is not (ColorMember or ParentMember))
            .Select(m => m.Key).ToList();

    /// <summary>Gives <paramref name="local"/> the label of <paramref name="remote"/> (a child key, §8.5.1).</summary>
    public static void CopyLabel(JsonObject remote, JsonObject local)
    {
        foreach (var member in LabelMembers(local)) local.Remove(member);
        foreach (var member in LabelMembers(remote)) local[member] = remote[member]!.DeepClone();
    }

    /// <summary>Sets a typed label: <c>label</c> where the child has one, else <c>name</c> (a tag keeps its group).</summary>
    public static void SetLabel(JsonObject local, string value)
    {
        var member = local.ContainsKey("label") ? "label" : local.ContainsKey("name") ? "name" : "label";
        local[member] = value;
    }

    /// <summary>A scalar member by dotted path (<c>name</c>, <c>settings.precision</c>); null when absent.</summary>
    public static JsonNode? GetPath(JsonObject content, string path)
    {
        JsonNode? node = content;
        foreach (var member in path.Split('.'))
        {
            if (node is not JsonObject o || !o.TryGetPropertyValue(member, out node)) return null;
        }

        return node;
    }

    /// <summary>Sets (or, with null, removes) a scalar member by dotted path, creating the objects on the way.</summary>
    public static void SetPath(JsonObject content, string path, JsonNode? value)
    {
        var members = path.Split('.');
        var owner = content;
        for (var i = 0; i < members.Length - 1; i++)
        {
            if (owner[members[i]] is not JsonObject next)
            {
                if (value is null) return;
                owner[members[i]] = next = new JsonObject();
            }

            owner = next;
        }

        if (value is null) owner.Remove(members[^1]);
        else owner[members[^1]] = value.DeepClone();
    }
}
