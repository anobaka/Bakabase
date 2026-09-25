using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Refs;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>
/// <c>defaultValue</c> refs within one content (§3.2): a ref resolves to the option with its uuid, else to the
/// representative of the class with an equal key (the first in list order, v3.1 §3.3.1). Tags have no default value.
/// </summary>
internal static class CustomPropertyRefs
{
    /// <summary>What a ref resolves to: the option's uuid and its class key (a node: the key path from the root).</summary>
    public sealed record Resolved(string Uuid, IReadOnlyList<string> KeyPath);

    /// <summary>Resolves <paramref name="optionRef"/> against <paramref name="content"/>'s options, or null.</summary>
    public static Resolved? Resolve(CustomPropertyContentV1 content, OptionRef optionRef)
    {
        var ignoreCase = content.IgnoreCase == true;
        switch (content.Type)
        {
            case PropertyType.SingleChoice or PropertyType.MultipleChoice:
            {
                var byUuid = content.Choices.FirstOrDefault(c => c.Uuid is not null && c.Uuid == optionRef.Uuid);
                if (byUuid is not null) return new Resolved(byUuid.Uuid!, [ChildClasses.KeyOf(byUuid, ignoreCase)]);
                if (optionRef.Label is null) return null;
                var key = DataSyncLabelKey.Fold(optionRef.Label, ignoreCase);
                var byKey = content.Choices.FirstOrDefault(c => c.Uuid is not null && ChildClasses.KeyOf(c, ignoreCase) == key);
                return byKey is null ? null : new Resolved(byKey.Uuid!, [key]);
            }
            case PropertyType.Multilevel:
            {
                var byUuid = FindNodePath(content.Nodes, n => n.Uuid is not null && n.Uuid == optionRef.Uuid);
                if (byUuid is not null)
                    return new Resolved(byUuid[^1].Uuid!, byUuid.Select(n => ChildClasses.KeyOf(n, ignoreCase)).ToArray());
                if (optionRef.Path is null) return null;
                var keys = optionRef.Path.Select(l => DataSyncLabelKey.Fold(l, ignoreCase)).ToArray();
                var byPath = FindByKeyPath(content.Nodes, keys, ignoreCase);
                return byPath?.Uuid is null ? null : new Resolved(byPath.Uuid, keys);
            }
            default:
                return null;
        }
    }

    /// <summary>The ref for the option with <paramref name="uuid"/> in <paramref name="content"/>, or null.</summary>
    public static OptionRef? RefFor(CustomPropertyContentV1 content, string uuid)
    {
        switch (content.Type)
        {
            case PropertyType.SingleChoice or PropertyType.MultipleChoice:
                var choice = content.Choices.FirstOrDefault(c => c.Uuid == uuid);
                return choice is null ? null : OptionRef.Choice(uuid, choice.Label);
            case PropertyType.Multilevel:
                var path = FindNodePath(content.Nodes, n => n.Uuid == uuid);
                return path is null ? null : OptionRef.Node(uuid, path.Select(n => n.Label).ToArray());
            default:
                return null;
        }
    }

    /// <summary>
    /// Maps every ref's uuid through <paramref name="aliases"/>, keeps the first ref per resulting uuid, and rebuilds
    /// each ref from <paramref name="content"/> when the option is there (a folded node may have a new parent).
    /// </summary>
    public static IReadOnlyList<OptionRef> Remap(CustomPropertyContentV1 content, IReadOnlyList<OptionRef> refs,
        IReadOnlyDictionary<string, string> aliases)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<OptionRef>();
        foreach (var optionRef in refs)
        {
            var uuid = aliases.GetValueOrDefault(optionRef.Uuid, optionRef.Uuid);
            if (!seen.Add(uuid)) continue;
            result.Add(RefFor(content, uuid) ?? optionRef);
        }

        return result;
    }

    /// <summary>The path from a root to the first node (pre-order) that matches, or null.</summary>
    public static IReadOnlyList<CustomPropertyNodeV1>? FindNodePath(IReadOnlyList<CustomPropertyNodeV1> nodes,
        Func<CustomPropertyNodeV1, bool> match)
    {
        var stack = new List<CustomPropertyNodeV1>();
        return Walk(nodes) ? stack : null;

        bool Walk(IReadOnlyList<CustomPropertyNodeV1> level)
        {
            foreach (var node in level)
            {
                stack.Add(node);
                if (match(node) || Walk(node.Children)) return true;
                stack.RemoveAt(stack.Count - 1);
            }

            return false;
        }
    }

    /// <summary>The representative of the class at <paramref name="keys"/>, walking classes level by level.</summary>
    private static CustomPropertyNodeV1? FindByKeyPath(IReadOnlyList<CustomPropertyNodeV1> roots, IReadOnlyList<string> keys,
        bool ignoreCase)
    {
        IReadOnlyList<NodeClass> level = ChildClasses.OfNodes(roots, ignoreCase);
        NodeClass? found = null;
        foreach (var key in keys)
        {
            found = level.FirstOrDefault(c => c.Key == key);
            if (found is null) return null;
            level = found.Children;
        }

        // The representative may have no uuid; the class's first member that has one stands for it.
        return found?.Members.FirstOrDefault(m => m.Uuid is not null);
    }
}
