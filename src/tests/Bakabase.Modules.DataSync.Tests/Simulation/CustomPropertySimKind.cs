using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Refs;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

/// <summary>
/// Custom properties (package B's codec) as the simulator runs them (§13.3): choices, tags and multilevel nodes with
/// few labels, so classes meet — case variants that one class under IgnoreCase, exact duplicates, tag groups among
/// <c>null</c>, <c>""</c> and two casings — plus a text and a number type to change to and from.
/// </summary>
/// <remarks>
/// <para>
/// The service is emulated as the adapter meets it (§14.2, B's fixes): a person's write goes through the Property
/// service's normalization (<see cref="Written"/>: <c>AddRange</c> folds with no preserved ids, <c>Put</c> preserves
/// every stored id, which is <see cref="OptionFolding"/>), while data sync's own writes are stored exactly as given
/// (<see cref="Store"/>: <c>PutVerbatim</c>, <c>AddRange</c> of content <c>PrepareCreate</c> already folded,
/// <c>AddRangeVerbatim</c> for an undo's re-create). "Sync the definition only" is the side row's, never the row's.
/// </para>
/// <para>
/// Every option this kind writes has a uuid; options that are never published (§3.3) come from a colour past the
/// reader's limit or an empty label, which the service stores like any other.
/// </para>
/// </remarks>
internal sealed class CustomPropertySimKind : SimKind
{
    public static CustomPropertySimKind Instance { get; } = new();

    /// <summary>Few labels, several of them case variants of each other (one class under IgnoreCase).</summary>
    public static readonly string[] Labels = ["Action", "action", "Drama", "Comedy", "COMEDY", "Horror", "É", "é"];

    /// <summary>Tag groups: none (null), none again (<c>""</c>, stored as null by the service), and two casings.</summary>
    public static readonly string?[] Groups = [null, "", "Studio", "studio"];

    public static readonly string?[] Colors = [null, null, "#e5484d", "#30a46c", "#0090ff"];

    /// <summary>A colour the reader drops (past 64 characters): the option is kept here and never published.</summary>
    public static readonly string UnpublishableColor = new('c', 65);

    private static readonly PropertyType[] ReferenceTypes =
        [PropertyType.SingleChoice, PropertyType.MultipleChoice, PropertyType.Tags, PropertyType.Multilevel];

    private static readonly PropertyType[] OtherTypes = [PropertyType.SingleLineText, PropertyType.Number];

    public override IDataSyncKindCodec Codec => CustomPropertyCodec.Instance;
    public override IReadOnlyList<string> Names { get; } = ["Genre", "Mood", "Artist", "Studio", "Series"];

    // ---- the service ----------------------------------------------------------------------------

    /// <summary>
    /// Data sync's writes are verbatim (<c>PutVerbatim</c>; a create is <c>PrepareCreate</c>'s already folded content;
    /// an undo's re-create is <c>AddRangeVerbatim</c>). The row has no "Sync the definition only": the side row keeps it.
    /// </summary>
    public override object Store(object content, object? stored) => Cp(content) with { ChildrenLocal = false };

    /// <summary>
    /// A person's write through the Property service (§3.3.2, F72): <c>AddRange</c> (nothing stored) folds case
    /// duplicates with no preserved ids; <c>Put</c> preserves every stored id and folds only options the write
    /// introduced. Nothing happens unless IgnoreCase is on.
    /// </summary>
    public override object Written(object content, object? stored)
    {
        var c = Cp(content) with { ChildrenLocal = false };
        var preserved = stored is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : Codec.ChildrenOf(stored).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        return OptionFolding.Fold(c, preserved).Content;
    }

    /// <summary>"Sync the definition only" means something for the four reference types alone (§3.6).</summary>
    public override bool OffersChildrenLocal(object content) => CustomPropertyTypes.IsReference(Cp(content).Type);

    // ---- local edits ----------------------------------------------------------------------------

    public override object NewContent(Random random, string name, Func<string> freshChildId)
    {
        var type = random.Next(8) == 0 ? Pick(random, OtherTypes) : Pick(random, ReferenceTypes);
        var content = Empty(name, type, random.Next(2) == 0);
        if (!CustomPropertyTypes.IsReference(type)) return content;
        var roots = Enumerable.Range(0, random.Next(0, 5)).Select(_ => NewOpt(random, type, freshChildId, 1)).ToList();
        content = FromOpts(content, roots);
        return random.Next(3) == 0 ? WithDefault(random, content) : content;
    }

    /// <summary>The edits a person makes, weighted; each is drawn only where it applies (see <see cref="RandomEdit"/>).</summary>
    private enum Edit
    {
        Rename, ToggleIgnoreCase, AddOption, RenameOption, Recolor, RemoveOption, MoveNode, EditTagGroup, ChangeType,
        ChangeSettings, ChangeDefault, AddMany, RemoveMany, AddUnpublishable,
    }

    private static readonly (Edit Edit, int Weight)[] EditWeights =
    [
        (Edit.Rename, 2), (Edit.ToggleIgnoreCase, 3), (Edit.AddOption, 4), (Edit.RenameOption, 3), (Edit.Recolor, 2),
        (Edit.RemoveOption, 2), (Edit.MoveNode, 3), (Edit.EditTagGroup, 3), (Edit.ChangeType, 1),
        (Edit.ChangeSettings, 1), (Edit.ChangeDefault, 1), (Edit.AddMany, 1), (Edit.RemoveMany, 1),
        (Edit.AddUnpublishable, 1),
    ];

    public override (string What, object Content)? RandomEdit(Random random, object content, Func<string> freshChildId)
    {
        var c = Cp(content);
        var reference = CustomPropertyTypes.IsReference(c.Type);
        var roots = ToOpts(c);
        var all = All(roots).ToList();
        bool Applies(Edit edit) => edit switch
        {
            Edit.ToggleIgnoreCase or Edit.AddOption or Edit.AddMany or Edit.AddUnpublishable => reference,
            Edit.RenameOption or Edit.Recolor or Edit.RemoveOption => all.Count > 0,
            Edit.MoveNode => c.Type == PropertyType.Multilevel && all.Count > 1,
            Edit.EditTagGroup => c.Type == PropertyType.Tags && all.Count > 0,
            Edit.ChangeSettings => c.Type is PropertyType.Multilevel or PropertyType.Number,
            Edit.ChangeDefault => CustomPropertyTypes.HasDefaultValue(c.Type) && all.Count > 0,
            Edit.RemoveMany => reference && all.Count >= 4,
            _ => true,
        };

        var applicable = EditWeights.Where(w => Applies(w.Edit)).ToArray();
        var pick = random.Next(applicable.Sum(w => w.Weight));
        var chosen = applicable.First(w => (pick -= w.Weight) < 0).Edit;
        switch (chosen)
        {
            case Edit.Rename:
                return ("rename", c with { Name = Pick(random, Names) + (random.Next(3) == 0 ? "!" : "") });
            case Edit.ToggleIgnoreCase:
                return ("toggleIgnoreCase", c with { IgnoreCase = !(c.IgnoreCase ?? false) });
            case Edit.AddOption:
            {
                // Often a case variant of a label already there: a duplicate for the next IgnoreCase toggle to meet.
                var added = NewOpt(random, c.Type, freshChildId, 3);
                if (all.Count > 0 && random.Next(3) == 0) added.Label = CaseVariant(Pick(random, all).Label);
                if (c.Type == PropertyType.Multilevel && all.Count > 0 && random.Next(2) == 0)
                    Pick(random, all).Children.Add(added);
                else roots.Add(added);
                return ("addOption", FromOpts(c, roots));
            }
            case Edit.RenameOption:
            {
                // Onto another option's label about half the time.
                var renamed = Pick(random, all);
                renamed.Label = random.Next(2) == 0 ? Pick(random, all).Label : Pick(random, Labels);
                return ("renameOption", FromOpts(c, roots));
            }
            case Edit.Recolor:
            {
                var recoloured = Pick(random, all);
                recoloured.Color = Pick(random, Colors);
                return (recoloured.Color is null ? "clearOptionColor" : "recolorOption", FromOpts(c, roots));
            }
            case Edit.RemoveOption:
            {
                var removed = Pick(random, all);
                Remove(roots, removed);
                return ("removeOption", WithoutDanglingDefaults(FromOpts(c, roots)));
            }
            case Edit.MoveNode:
            {
                var moved = Pick(random, all);
                Remove(roots, moved);
                var targets = All(roots).ToList();
                if (targets.Count > 0 && random.Next(3) != 0) Pick(random, targets).Children.Add(moved);
                else roots.Add(moved);
                return ("moveNode", FromOpts(c, roots));
            }
            case Edit.EditTagGroup:
            {
                var tag = Pick(random, all);
                var groups = Groups.Where(g => g != tag.Group).ToArray();
                tag.Group = Pick(random, groups);
                return ("editTagGroup", FromOpts(c, roots));
            }
            case Edit.ChangeType:
            {
                // Mostly between the reference types; now and then to or from a type without options.
                var types = random.Next(5) == 0 ? OtherTypes : ReferenceTypes;
                var type = Pick(random, types.Where(t => t != c.Type).ToArray());
                return ("changeType", ChangeSubtype(c, CustomPropertyTypes.NameOf(type), freshChildId));
            }
            case Edit.ChangeSettings when c.Type == PropertyType.Multilevel:
                return ("changeSettings", c with
                {
                    Settings = new CustomPropertySettingsV1 { ValueIsSingleton = !(c.Settings?.ValueIsSingleton ?? false) },
                });
            case Edit.ChangeSettings:
                return ("changeSettings", c with
                {
                    Settings = new CustomPropertySettingsV1 { Precision = ((c.Settings?.Precision ?? 0) + 1) % 4 },
                });
            case Edit.ChangeDefault:
                return ("changeDefault", WithDefault(random, c));
            case Edit.AddMany:
            {
                // An enhancer run: many options at once.
                for (var i = random.Next(4, 13); i > 0; i--)
                {
                    var added = NewOpt(random, c.Type, freshChildId, 3);
                    added.Label += random.Next(4).ToString(CultureInfo.InvariantCulture);
                    roots.Add(added);
                }

                return ("addMany", FromOpts(c, roots));
            }
            case Edit.RemoveMany:
            {
                // A clean-up: most options at once (B4 on the receivers).
                foreach (var removed in all.Where(_ => random.Next(4) != 0)) Remove(roots, removed);
                return ("removeMany", WithoutDanglingDefaults(FromOpts(c, roots)));
            }
            case Edit.AddUnpublishable:
            {
                // An option this device keeps but never publishes (§3.3): an empty label, or a colour the reader drops.
                var kept = NewOpt(random, c.Type, freshChildId, 3);
                if (random.Next(2) == 0) kept.Label = "";
                else kept.Color = UnpublishableColor;
                roots.Insert(random.Next(roots.Count + 1), kept);
                return ("addUnpublishable", FromOpts(c, roots));
            }
            default:
                return null;
        }
    }

    public override object Renamed(object content, string name) => Cp(content) with { Name = name };

    public override object WithoutChildren(object content, IReadOnlyCollection<string> ids)
    {
        var c = Cp(content);
        var roots = ToOpts(c);
        foreach (var opt in All(roots).Where(o => o.Id is not null && ids.Contains(o.Id)).ToList()) Remove(roots, opt);
        return WithoutDanglingDefaults(FromOpts(c, roots));
    }

    // ---- resolutions (§9.2) ----------------------------------------------------------------------

    /// <summary>
    /// One conflicting path set to the peer's value or a typed one, as the Business runner's
    /// <c>DataSyncFieldEdits</c> does on canonical JSON: <c>name</c>, <c>ignoreCase</c>, <c>settings.*</c> and
    /// <c>defaultValue</c> (translated through the child map), a class key <c>{noun}:{peerId}</c> (the label, and a
    /// tag's group) and a node's parent <c>node:{peerId}:parent</c> (never under itself).
    /// </summary>
    public override object? SetField(object local, string path, object? remote, string? custom,
        IReadOnlyDictionary<string, string> childMap)
    {
        var c = Cp(local);
        var theirs = remote as CustomPropertyContentV1;
        switch (path)
        {
            case "name":
                return c with { Name = custom ?? theirs?.Name ?? c.Name };
            case "ignoreCase":
                return theirs is null ? null : c with { IgnoreCase = theirs.IgnoreCase };
            case "defaultValue":
            {
                if (theirs is null) return null;
                var mapped = theirs.DefaultValue
                    .Select(r => childMap.GetValueOrDefault(r.Uuid) ?? r.Uuid)
                    .Distinct(StringComparer.Ordinal)
                    .Select(id => CustomPropertyRefs.RefFor(c, id))
                    .OfType<OptionRef>()
                    .ToArray();
                return c with { DefaultValue = mapped };
            }
        }

        if (path.StartsWith("settings.", StringComparison.Ordinal))
        {
            if (theirs is null || theirs.Type != c.Type) return null;
            var json = Codec.Write(c);
            var value = Codec.Write(theirs)["settings"]?[path["settings.".Length..]]?.DeepClone();
            if (json["settings"] is not JsonObject settings) json["settings"] = settings = new JsonObject();
            settings[path["settings.".Length..]] = value;
            return Codec.ReadLocal(json);
        }

        var separator = path.IndexOf(':');
        if (separator < 0 || theirs is null) return null;
        var parent = path.EndsWith(":parent", StringComparison.Ordinal);
        var peerId = parent ? path[(separator + 1)..^":parent".Length] : path[(separator + 1)..];
        var remoteRoots = ToOpts(theirs);
        var remoteOpt = All(remoteRoots).FirstOrDefault(o => o.Id == peerId);
        var roots = ToOpts(c);
        var localId = childMap.GetValueOrDefault(peerId) ?? peerId;
        var ours = All(roots).FirstOrDefault(o => o.Id == localId);
        if (remoteOpt is null || ours is null) return null;
        if (!parent)
        {
            ours.Label = custom ?? remoteOpt.Label;
            if (custom is null && c.Type == PropertyType.Tags) ours.Group = remoteOpt.Group;
            return FromOpts(c, roots);
        }

        if (custom is not null || c.Type != PropertyType.Multilevel) return null;
        var remoteParent = ParentOf(remoteRoots, remoteOpt);
        Opt? target = null;
        if (remoteParent is not null)
        {
            var parentId = childMap.GetValueOrDefault(remoteParent.Id!) ?? remoteParent.Id;
            target = All(roots).FirstOrDefault(o => o.Id == parentId);
            // A move never puts a node under itself or below it (§8.5.4).
            if (target is null || All([ours]).Contains(target)) return null;
        }

        Remove(roots, ours);
        (target?.Children ?? roots).Add(ours);
        return FromOpts(c, roots);
    }

    /// <summary>
    /// F73: the service changes the type and rebuilds the options from this device's values — here, from the labels
    /// the options had — with fresh ids and no colours, folded as a new property's options are.
    /// </summary>
    public override object ChangeSubtype(object content, string? subtype, Func<string> freshChildId)
    {
        var c = Cp(content);
        if (subtype is null || !CustomPropertyTypes.TryParse(subtype, out var type)) return c;
        var reference = CustomPropertyTypes.IsReference(type);
        var labels = All(ToOpts(c)).Select(o => o.Label).Where(l => l.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var rebuilt = Empty(c.Name, type, reference && (c.IgnoreCase ?? false));
        if (reference)
            rebuilt = FromOpts(rebuilt, labels.Select(l => new Opt { Id = freshChildId(), Label = l }).ToList());
        return Written(rebuilt, null);
    }

    // ---- undo and the lost-update guard -----------------------------------------------------------

    /// <summary>
    /// Undo of an applied update (§8.11 pre-images version 2): every changed path back, refused when one of them
    /// changed since (<c>changedSinceImport</c>). A type change is converted back by the adapter's <c>RestoreAsync</c>,
    /// which the simulator does not model: refused.
    /// </summary>
    public override object? Revert(object current, DataSyncEntityChangeList applied, out string? refusal)
    {
        refusal = "changedSinceImport";
        var c = Cp(current);
        var json = Codec.Write(c);
        var scalars = DataSyncEntityChangeList.ScalarsOf(json);
        if (applied.Scalars.Any(s => s.Path == "type"))
        {
            refusal = "typeChanged";
            return null;
        }

        if (applied.Scalars.Any(s => !JsonNode.DeepEquals(scalars.GetValueOrDefault(s.Path), s.After))) return null;
        var roots = ToOpts(c);
        var byId = All(roots).Where(o => o.Id is not null).GroupBy(o => o.Id!).ToDictionary(g => g.Key, g => g.First());
        var addedIds = applied.Added.Select(a => a.ChildId).ToHashSet(StringComparer.Ordinal);
        foreach (var added in applied.Added)
        {
            if (!byId.TryGetValue(added.ChildId, out var now) || now.Label != added.Display.Text ||
                now.Children.Any(child => child.Id is null || !addedIds.Contains(child.Id))) return null;
        }

        if (applied.Removed.Any(r => byId.ContainsKey(r.ChildId))) return null;
        if (applied.Renamed.Any(r => !byId.TryGetValue(r.ChildId, out var now) || now.Label != r.After.Text ||
                                     (c.Type == PropertyType.Tags && now.Group != r.After.Group))) return null;
        if (applied.Moved.Any(m => !byId.TryGetValue(m.ChildId, out var now) || ParentOf(roots, now)?.Id != m.ParentAfter))
            return null;

        refusal = null;
        foreach (var s in applied.Scalars) SetScalar(json, s.Path, s.Before);
        var reverted = Cp(Codec.ReadLocal(json));
        roots = ToOpts(reverted);
        byId = All(roots).Where(o => o.Id is not null).GroupBy(o => o.Id!).ToDictionary(g => g.Key, g => g.First());
        foreach (var added in applied.Added)
        {
            if (byId.Remove(added.ChildId, out var gone)) Remove(roots, gone);
        }

        foreach (var r in applied.Renamed)
        {
            byId[r.ChildId].Label = r.Before.Text ?? "";
            if (c.Type == PropertyType.Tags) byId[r.ChildId].Group = r.Before.Group;
        }

        foreach (var r in applied.Recolored.Where(r => byId.ContainsKey(r.ChildId))) byId[r.ChildId].Color = r.Before.Color;
        foreach (var m in applied.Moved) Move(roots, byId, byId[m.ChildId], m.ParentBefore);
        // Removed options come back, parents before their children.
        foreach (var removed in applied.Removed.OrderBy(r => r.Display.Path?.Count ?? 0))
        {
            var opt = new Opt { Id = removed.ChildId, Label = removed.Display.Text ?? "", Group = removed.Display.Group, Color = removed.Display.Color };
            byId[removed.ChildId] = opt;
            (removed.ParentId is { } p && byId.TryGetValue(p, out var parent) ? parent.Children : roots).Add(opt);
        }

        return FromOpts(reverted, roots);
    }

    /// <summary>§6.5 Reapply: only the undone changes written again, by id and by path.</summary>
    public override object Reapply(object current, DataSyncEntityChangeList undone)
    {
        var c = Cp(current);
        var json = Codec.Write(c);
        foreach (var s in undone.Scalars.Where(s => s.Path != "type")) SetScalar(json, s.Path, s.After);
        var reapplied = Cp(Codec.ReadLocal(json));
        var roots = ToOpts(reapplied);
        var byId = All(roots).Where(o => o.Id is not null).GroupBy(o => o.Id!).ToDictionary(g => g.Key, g => g.First());
        foreach (var removed in undone.Removed)
        {
            if (!byId.Remove(removed.ChildId, out var gone)) continue;
            // Its children that were not removed with it stay, under its parent.
            var parent = ParentOf(roots, gone);
            Remove(roots, gone);
            (parent?.Children ?? roots).AddRange(gone.Children.Where(child => child.Id is null || byId.ContainsKey(child.Id)));
        }

        foreach (var r in undone.Renamed.Where(r => byId.ContainsKey(r.ChildId)))
        {
            byId[r.ChildId].Label = r.After.Text ?? "";
            if (c.Type == PropertyType.Tags) byId[r.ChildId].Group = r.After.Group;
        }

        foreach (var m in undone.Moved.Where(m => byId.ContainsKey(m.ChildId))) Move(roots, byId, byId[m.ChildId], m.ParentAfter);
        foreach (var added in undone.Added.Where(a => !byId.ContainsKey(a.ChildId)).OrderBy(a => a.Display.Path?.Count ?? 0))
        {
            var opt = new Opt { Id = added.ChildId, Label = added.Display.Text ?? "", Group = added.Display.Group, Color = added.Display.Color };
            byId[added.ChildId] = opt;
            (added.ParentId is { } p && byId.TryGetValue(p, out var parent) ? parent.Children : roots).Add(opt);
        }

        return FromOpts(reapplied, roots);
    }

    // ---- reporting -------------------------------------------------------------------------------

    /// <summary>A property in one line: <c>Genre:Tags ic [Action#e5@c3, Studio/drama@c4, …] dv[c3]</c>.</summary>
    public override string Describe(object content)
    {
        var c = Cp(content);
        var text = new StringBuilder();
        text.Append(c.Name).Append(':').Append(c.Type);
        if (c.IgnoreCase == true) text.Append(" ic");
        if (c.Settings is { ValueIsSingleton: true }) text.Append(" single");
        if (c.Settings?.Precision is > 0 and var precision) text.Append(" p").Append(precision);
        if (CustomPropertyTypes.IsReference(c.Type))
        {
            text.Append(" [");
            Append(ToOpts(c));
            text.Append(']');
        }

        if (c.DefaultValue.Count > 0) text.Append(" dv[").Append(string.Join(",", c.DefaultValue.Select(r => r.Uuid))).Append(']');
        return text.ToString();

        void Append(List<Opt> level)
        {
            for (var i = 0; i < level.Count; i++)
            {
                var o = level[i];
                if (i > 0) text.Append(", ");
                if (o.Group is not null) text.Append(o.Group.Length == 0 ? "\"\"" : o.Group).Append('/');
                text.Append(o.Label.Length == 0 ? "∅" : o.Label);
                if (o.Color is { } color) text.Append(color.Length > 8 ? "#!" : color[..Math.Min(3, color.Length)]);
                text.Append('@').Append(o.Id ?? "?");
                if (o.Children.Count == 0) continue;
                text.Append('(');
                Append(o.Children);
                text.Append(')');
            }
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static CustomPropertyContentV1 Cp(object content) => (CustomPropertyContentV1)content;

    private static CustomPropertyContentV1 Empty(string name, PropertyType type, bool ignoreCase) => new()
    {
        Name = name, Type = type, IgnoreCase = CustomPropertyTypes.IsReference(type) ? ignoreCase : null,
        Settings = CustomPropertyTypes.DefaultSettings(type),
    };

    private static string CaseVariant(string label) =>
        label == label.ToUpperInvariant() ? label.ToLowerInvariant() : label.ToUpperInvariant();

    private Opt NewOpt(Random random, PropertyType type, Func<string> freshChildId, int depth)
    {
        var opt = new Opt
        {
            Id = freshChildId(), Label = Pick(random, Labels), Color = Pick(random, Colors),
            Group = type == PropertyType.Tags ? Pick(random, Groups) : null,
        };
        if (type == PropertyType.Multilevel && depth < 3)
        {
            for (var i = random.Next(depth == 1 ? 3 : 2); i > 0; i--) opt.Children.Add(NewOpt(random, type, freshChildId, depth + 1));
        }

        return opt;
    }

    private CustomPropertyContentV1 WithDefault(Random random, CustomPropertyContentV1 content)
    {
        if (!CustomPropertyTypes.HasDefaultValue(content.Type)) return content;
        var ids = Codec.ChildrenOf(content).Select(c => c.Id).ToList();
        if (ids.Count == 0) return content with { DefaultValue = [] };
        var count = content.Type == PropertyType.SingleChoice ? 1 : random.Next(0, 3);
        return content with
        {
            DefaultValue = Enumerable.Range(0, count).Select(_ => Pick(random, ids)).Distinct(StringComparer.Ordinal)
                .Select(id => CustomPropertyRefs.RefFor(content, id)).OfType<OptionRef>().ToArray(),
        };
    }

    private CustomPropertyContentV1 WithoutDanglingDefaults(CustomPropertyContentV1 content)
    {
        var ids = Codec.ChildrenOf(content).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        return content.DefaultValue.All(r => ids.Contains(r.Uuid))
            ? content
            : content with { DefaultValue = content.DefaultValue.Where(r => ids.Contains(r.Uuid)).ToArray() };
    }

    private static void SetScalar(JsonObject json, string path, JsonNode? value)
    {
        var dot = path.IndexOf('.');
        if (dot < 0)
        {
            if (value is null) json.Remove(path);
            else json[path] = value.DeepClone();
            return;
        }

        var member = path[..dot];
        if (json[member] is not JsonObject nested) json[member] = nested = new JsonObject();
        if (value is null) nested.Remove(path[(dot + 1)..]);
        else nested[path[(dot + 1)..]] = value.DeepClone();
    }

    private static void Move(List<Opt> roots, Dictionary<string, Opt> byId, Opt node, string? parentId)
    {
        var target = parentId is null ? null : byId.GetValueOrDefault(parentId);
        if (parentId is not null && target is null) return;
        if (target is not null && All([node]).Contains(target)) return;
        Remove(roots, node);
        (target?.Children ?? roots).Add(node);
    }

    private static Opt? ParentOf(List<Opt> roots, Opt node) => All(roots).FirstOrDefault(o => o.Children.Contains(node));

    private static void Remove(List<Opt> roots, Opt target)
    {
        if (roots.Remove(target)) return;
        foreach (var node in All(roots))
        {
            if (node.Children.Remove(target)) return;
        }
    }

    private static IEnumerable<Opt> All(IEnumerable<Opt> roots) => roots.SelectMany(r => new[] { r }.Concat(All(r.Children)));

    private static List<Opt> ToOpts(CustomPropertyContentV1 content) => content.Type switch
    {
        PropertyType.Tags => content.Tags.Select(t => new Opt { Id = t.Uuid, Label = t.Name, Group = t.Group, Color = t.Color }).ToList(),
        PropertyType.Multilevel => FromTree(content.Nodes),
        PropertyType.SingleChoice or PropertyType.MultipleChoice =>
            content.Choices.Select(c => new Opt { Id = c.Uuid, Label = c.Label, Color = c.Color }).ToList(),
        _ => [],
    };

    private static List<Opt> FromTree(IReadOnlyList<CustomPropertyNodeV1> nodes) =>
        nodes.Select(n =>
        {
            var opt = new Opt { Id = n.Uuid, Label = n.Label, Color = n.Color };
            opt.Children.AddRange(FromTree(n.Children));
            return opt;
        }).ToList();

    private static CustomPropertyContentV1 FromOpts(CustomPropertyContentV1 content, List<Opt> roots) => content.Type switch
    {
        PropertyType.Tags => content with { Choices = [], Nodes = [], Tags = roots.Select(o => new CustomPropertyTagV1(o.Id, o.Group, o.Label, o.Color)).ToArray() },
        PropertyType.Multilevel => content with { Choices = [], Tags = [], Nodes = ToTree(roots) },
        PropertyType.SingleChoice or PropertyType.MultipleChoice =>
            content with { Tags = [], Nodes = [], Choices = roots.Select(o => new CustomPropertyChoiceV1(o.Id, o.Label, o.Color)).ToArray() },
        _ => content with { Choices = [], Tags = [], Nodes = [], DefaultValue = [] },
    };

    private static IReadOnlyList<CustomPropertyNodeV1> ToTree(IEnumerable<Opt> nodes) =>
        nodes.Select(o => new CustomPropertyNodeV1(o.Id, o.Label, o.Color) { Children = ToTree(o.Children) }).ToArray();

    private sealed class Opt
    {
        public required string? Id { get; set; }
        public required string Label { get; set; }
        public string? Group { get; set; }
        public string? Color { get; set; }
        public List<Opt> Children { get; } = [];
    }
}
