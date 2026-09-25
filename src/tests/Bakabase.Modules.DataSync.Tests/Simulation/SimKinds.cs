using System.Globalization;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Tests.TestKinds;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

/// <summary>
/// What the simulator needs to know about one kind beyond its codec: the adapter and the service it writes
/// through (what a local edit can do, what the service keeps when content is written), and the kind-specific
/// halves of resolutions, undo and the lost-update guard's Reapply. Every engine decision still comes from the
/// codec and the shared helpers; a kind added later (B's custom property kind) plugs in here.
/// </summary>
internal abstract class SimKind
{
    public abstract IDataSyncKindCodec Codec { get; }
    public string Kind => Codec.Descriptor.Kind;
    public bool HasOrder => Codec.Descriptor.HasOrder;

    /// <summary>Entity names local edits pick from; a small pool, so separate creates meet as name matches.</summary>
    public abstract IReadOnlyList<string> Names { get; }

    /// <summary>
    /// What the service stores when content is written (the adapter's <c>Put</c> for an update with the stored
    /// content, <c>AddRange</c> for a create with none). The simulator re-reads this after every apply (§6.4).
    /// </summary>
    public virtual object Store(object content, object? stored) => content;

    public abstract object NewContent(Random random, string name, Func<string> freshChildId);

    /// <summary>One random local edit of the content, or null when none applies.</summary>
    public abstract (string What, object Content)? RandomEdit(Random random, object content, Func<string> freshChildId);

    public abstract object Renamed(object content, string name);

    /// <summary>The children a resource value may use (ids).</summary>
    public virtual IReadOnlyList<string> UsableChildIds(object content) =>
        Codec.ChildrenOf(content).Select(c => c.Id).Where(id => id.Length > 0).ToList();

    public abstract object WithoutChildren(object content, IReadOnlyCollection<string> ids);

    /// <summary>
    /// A resolution's value for one conflicting path (§9.2): the peer's (<paramref name="remote"/>, its child
    /// mapped through <paramref name="childMap"/>) or the custom input. Null when the path no longer exists on
    /// either side (the card is stale): nothing changes.
    /// </summary>
    public abstract object? SetField(object local, string path, object? remote, string? custom,
        IReadOnlyDictionary<string, string> childMap);

    /// <summary>Convert, phase one (§8.5.6): the service changes the subtype and rebuilds children (F73).</summary>
    public virtual object ChangeSubtype(object content, string? subtype, Func<string> freshChildId) => content;

    /// <summary>
    /// Undo of an applied update (§8.11 pre-images version 2): the changed paths back to their before values.
    /// Null with a refusal (<c>ChangedSinceImport</c>) when one of those paths changed since.
    /// </summary>
    public abstract object? Revert(object current, DataSyncEntityChangeList applied, out string? refusal);

    /// <summary>§6.5 Reapply: only the undone changes written again, by id and by path.</summary>
    public abstract object Reapply(object current, DataSyncEntityChangeList undone);

    public string NameOf(object content) => Codec.NameOf(content);

    protected static T Pick<T>(Random random, IReadOnlyList<T> items) => items[random.Next(items.Count)];
}

/// <summary>The generic test kind: a name, a colour, a subtype, an order key, and children with ids and labels.</summary>
internal sealed class TestItemSimKind : SimKind
{
    public static TestItemSimKind Instance { get; } = new();

    public static readonly string[] Labels = ["Action", "Drama", "Comedy", "Horror", "Isekai", "Mecha", "Slice", "Sports"];
    public static readonly string?[] Types = [null, "Tags", "Choice"];
    public static readonly string?[] Colors = [null, "#e5484d", "#30a46c", "#0090ff"];

    public override IDataSyncKindCodec Codec => TestItemCodec.Instance;
    public override IReadOnlyList<string> Names { get; } = ["Genre", "Mood", "Artist", "Studio", "Rating", "Series"];

    public override object NewContent(Random random, string name, Func<string> freshChildId)
    {
        var children = Enumerable.Range(0, random.Next(0, 4)).Select(_ => new TestChild(freshChildId(), Pick(random, Labels)));
        return new TestItemContent(name, Pick(random, Colors), children, Pick(random, Types));
    }

    public override (string What, object Content)? RandomEdit(Random random, object content, Func<string> freshChildId)
    {
        var item = (TestItemContent)content;
        switch (random.Next(10))
        {
            case 8:
                // An enhancer run: many options at once.
                return ("addMany", item.With(children: item.Children.Concat(Enumerable.Range(0, random.Next(4, 13))
                    .Select(_ => new TestChild(freshChildId(), Pick(random, Labels) + random.Next(4))))));
            case 9 when item.Children.Count >= 4:
                // A clean-up: most options at once (B4 on the receivers).
                var keep = item.Children.Where(_ => random.Next(4) == 0).ToList();
                return ("removeMany", item.With(children: keep));
            case 0:
                return ("rename", item.With(name: Pick(random, Names) + (random.Next(3) == 0 ? "!" : "")));
            case 1:
                var color = Pick(random, Colors);
                return (color is null ? "clearColor" : "recolor", color is null ? item.With(clearColor: true) : item.With(color: color));
            case 2:
                var type = Pick(random, Types);
                return type == item.Type ? null : ("changeType", new TestItemContent(item.Name, item.Color, item.Children, type));
            case 3 or 4:
                return ("addChild", item.With(children: [.. item.Children, new TestChild(freshChildId(), Pick(random, Labels))]));
            case 5 when item.Children.Count > 0:
                var at = random.Next(item.Children.Count);
                return ("renameChild", item.With(children: item.Children.Select((c, i) => i == at ? c with { Label = Pick(random, Labels) } : c)));
            case 6 when item.Children.Count > 0:
                var gone = item.Children[random.Next(item.Children.Count)].Id;
                return ("removeChild", item.With(children: item.Children.Where(c => c.Id != gone)));
            default:
                return null;
        }
    }

    public override object Renamed(object content, string name) => ((TestItemContent)content).With(name: name);

    public override object WithoutChildren(object content, IReadOnlyCollection<string> ids)
    {
        var item = (TestItemContent)content;
        return item.With(children: item.Children.Where(c => !ids.Contains(c.Id)));
    }

    public override object? SetField(object local, string path, object? remote, string? custom,
        IReadOnlyDictionary<string, string> childMap)
    {
        var item = (TestItemContent)local;
        var theirs = remote as TestItemContent;
        if (path == "name") return item.With(name: custom ?? theirs?.Name ?? item.Name);
        if (!path.StartsWith(TestItemCodec.ChildPathPrefix, StringComparison.Ordinal)) return null;
        var peerId = path[TestItemCodec.ChildPathPrefix.Length..];
        var localId = childMap.GetValueOrDefault(peerId) ?? peerId;
        var label = custom ?? theirs?.Children.FirstOrDefault(c => c.Id == peerId)?.Label;
        if (label is null || item.Children.All(c => c.Id != localId)) return null;
        return item.With(children: item.Children.Select(c => c.Id == localId ? c with { Label = label } : c));
    }

    /// <summary>F73: the options are rebuilt from this device's values, with fresh ids.</summary>
    public override object ChangeSubtype(object content, string? subtype, Func<string> freshChildId)
    {
        var item = (TestItemContent)content;
        return new TestItemContent(item.Name, item.Color, item.Children.Select(c => new TestChild(freshChildId(), c.Label)), subtype);
    }

    public override object? Revert(object current, DataSyncEntityChangeList applied, out string? refusal)
    {
        refusal = null;
        var item = (TestItemContent)current;
        var json = TestItemCodec.Instance.Write(item);
        foreach (var scalar in applied.Scalars)
        {
            if (!System.Text.Json.Nodes.JsonNode.DeepEquals(json[scalar.Path], scalar.After))
            {
                refusal = "changedSinceImport";
                return null;
            }
        }

        var children = item.Children.ToList();
        foreach (var added in applied.Added)
        {
            var now = children.FirstOrDefault(c => c.Id == added.ChildId);
            if (now is not null && now.Label != added.Display.Text)
            {
                refusal = "changedSinceImport";
                return null;
            }
        }

        if (applied.Removed.Any(r => children.Any(c => c.Id == r.ChildId)) ||
            applied.Renamed.Any(r => children.FirstOrDefault(c => c.Id == r.ChildId) is { } c && c.Label != r.After.Text))
        {
            refusal = "changedSinceImport";
            return null;
        }

        var name = item.Name;
        var color = item.Color;
        var type = item.Type;
        foreach (var scalar in applied.Scalars)
        {
            var before = scalar.Before?.GetValue<string>();
            switch (scalar.Path)
            {
                case "name": name = before ?? name; break;
                case "color": color = before; break;
                case "type": type = before; break;
            }
        }

        children.RemoveAll(c => applied.Added.Any(a => a.ChildId == c.Id));
        children = children.Select(c => applied.Renamed.FirstOrDefault(r => r.ChildId == c.Id) is { } r
            ? c with { Label = r.Before.Text! }
            : c).ToList();
        children.AddRange(applied.Removed.Select(r => new TestChild(r.ChildId, r.Display.Text!)));
        return new TestItemContent(name, color, children, type);
    }

    public override object Reapply(object current, DataSyncEntityChangeList undone)
    {
        var item = (TestItemContent)current;
        var name = item.Name;
        var color = item.Color;
        var type = item.Type;
        foreach (var scalar in undone.Scalars)
        {
            var after = scalar.After?.GetValue<string>();
            switch (scalar.Path)
            {
                case "name": name = after ?? name; break;
                case "color": color = after; break;
                case "type": type = after; break;
            }
        }

        var children = item.Children
            .Where(c => undone.Removed.All(r => r.ChildId != c.Id))
            .Select(c => undone.Renamed.FirstOrDefault(r => r.ChildId == c.Id) is { } r ? c with { Label = r.After.Text! } : c)
            .ToList();
        children.AddRange(undone.Added.Where(a => children.All(c => c.Id != a.ChildId))
            .Select(a => new TestChild(a.ChildId, a.Display.Text!)));
        return new TestItemContent(name, color, children, type);
    }
}

/// <summary>Extension groups: a name and a set of canonical extensions (values, no ids).</summary>
internal sealed class ExtensionGroupSimKind : SimKind
{
    public static ExtensionGroupSimKind Instance { get; } = new();

    public static readonly string[] Pool = [".mp4", ".mkv", ".avi", ".mp3", ".flac", ".jpg", ".png", ".zip"];

    public override IDataSyncKindCodec Codec => ExtensionGroupCodec.Instance;
    public override IReadOnlyList<string> Names { get; } = ["Video", "Audio", "Image", "Archive"];

    public override object NewContent(Random random, string name, Func<string> freshChildId) =>
        new ExtensionGroupContentV1(name, Pool.Where(_ => random.Next(3) == 0));

    public override (string What, object Content)? RandomEdit(Random random, object content, Func<string> freshChildId)
    {
        var group = (ExtensionGroupContentV1)content;
        switch (random.Next(3))
        {
            case 0:
                return ("rename", new ExtensionGroupContentV1(Pick(random, Names) + (random.Next(3) == 0 ? "!" : ""), group.Extensions));
            case 1:
                var add = Pick(random, Pool);
                return group.Extensions.Contains(add) ? null : ("addExtension", new ExtensionGroupContentV1(group.Name, [.. group.Extensions, add]));
            default:
                if (group.Extensions.Count == 0) return null;
                var gone = Pick(random, group.Extensions);
                return ("removeExtension", new ExtensionGroupContentV1(group.Name, group.Extensions.Where(e => e != gone)));
        }
    }

    public override object Renamed(object content, string name) =>
        new ExtensionGroupContentV1(name, ((ExtensionGroupContentV1)content).Extensions);

    /// <summary>Extensions are never in use (§8.5.3): removing one never depends on usage.</summary>
    public override IReadOnlyList<string> UsableChildIds(object content) => [];

    public override object WithoutChildren(object content, IReadOnlyCollection<string> ids)
    {
        var group = (ExtensionGroupContentV1)content;
        return new ExtensionGroupContentV1(group.Name, group.Extensions.Where(e => !ids.Contains(e)));
    }

    public override object? SetField(object local, string path, object? remote, string? custom,
        IReadOnlyDictionary<string, string> childMap)
    {
        var group = (ExtensionGroupContentV1)local;
        if (path != "name") return null;
        var name = custom ?? (remote as ExtensionGroupContentV1)?.Name ?? group.Name;
        return new ExtensionGroupContentV1(name, group.Extensions);
    }

    public override object? Revert(object current, DataSyncEntityChangeList applied, out string? refusal)
    {
        refusal = null;
        var group = (ExtensionGroupContentV1)current;
        var name = group.Name;
        foreach (var scalar in applied.Scalars.Where(s => s.Path == "name"))
        {
            if (scalar.After?.GetValue<string>() != group.Name)
            {
                refusal = "changedSinceImport";
                return null;
            }

            name = scalar.Before?.GetValue<string>() ?? name;
        }

        var set = group.Extensions.ToHashSet(StringComparer.Ordinal);
        set.ExceptWith(applied.Added.Select(a => a.ChildId));
        set.UnionWith(applied.Removed.Select(r => r.ChildId));
        return new ExtensionGroupContentV1(name, set);
    }

    public override object Reapply(object current, DataSyncEntityChangeList undone)
    {
        var group = (ExtensionGroupContentV1)current;
        var name = undone.Scalars.FirstOrDefault(s => s.Path == "name")?.After?.GetValue<string>() ?? group.Name;
        var set = group.Extensions.ToHashSet(StringComparer.Ordinal);
        set.UnionWith(undone.Added.Select(a => a.ChildId));
        set.ExceptWith(undone.Removed.Select(r => r.ChildId));
        return new ExtensionGroupContentV1(name, set);
    }
}

internal static class SimFormat
{
    public static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);
}
