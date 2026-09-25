using System.Globalization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Refs;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>
/// <c>Merge3</c> of one custom property (§8.5): the subtype first (§8.5.6), then the scalar paths (§8.5.2), the
/// children (<see cref="ChildMerge3"/>, §8.5.4 with §8.5.5's colours) and <c>defaultValue</c>, translated through the
/// class map (step 9). The result is folded as the service will fold it (<see cref="OptionFolding"/>, every local id
/// preserved), so it lists exactly what a <c>Put</c> stores.
/// </summary>
/// <remarks>
/// <c>orderKey</c> and the preserved unknown members (§8.9) are not content: they travel beside it, and the merger
/// merges them. <c>Merged.ChildrenLocal</c> is set only when the merged value differs from this device's; the
/// <c>childrenLocal</c> outcome carries the merged value, and without one it is this device's.
/// </remarks>
internal sealed class CustomPropertyMerge
{
    private const string NamePath = CustomPropertyJson.Name;
    private const string TypePath = CustomPropertyJson.Type;
    private const string IgnoreCasePath = CustomPropertyJson.IgnoreCase;
    private const string ChildrenLocalPath = CustomPropertyJson.ChildrenLocal;
    private const string DefaultValuePath = CustomPropertyJson.DefaultValue;

    private readonly CustomPropertyCodec _codec;
    private readonly CustomPropertyContentV1? _base;
    private readonly CustomPropertyContentV1 _local;
    private readonly CustomPropertyContentV1 _remote;
    private readonly DataSyncMerge3Input _input;
    private readonly DataSyncAutoApplyPolicy _policy;
    private readonly List<DataSyncFieldOutcome> _fields = [];
    private readonly List<DataSyncPlanWarning> _warnings = [];

    // Scalars as they merge.
    private string _name = null!;
    private bool? _ignoreCase;
    private bool _ignoreCaseChanged;
    private bool _childrenLocal;
    private bool _childrenLocalChanged;
    private CustomPropertySettingsV1? _settings;
    private bool _settingsChanged;
    private bool _childrenForcedNoBase;

    public CustomPropertyMerge(CustomPropertyCodec codec, CustomPropertyContentV1? baseContent,
        CustomPropertyContentV1 local, CustomPropertyContentV1 remote, DataSyncMerge3Input input,
        DataSyncAutoApplyPolicy policy)
    {
        _codec = codec;
        _base = baseContent;
        _local = local;
        _remote = remote;
        _input = input;
        _policy = policy;
    }

    public DataSyncMerge3Result Run()
    {
        if (TypeChangedThere()) return TypeChangeHeld();
        MergeScalars();

        var children = PlanChildren();
        ChildMergeOutcome? outcome = null;
        if (children is not null)
        {
            children.Engine.Plan();
            outcome = children.Engine.Finish(_input.LocalChildUsage);
            if (outcome.MassDeletionCandidates.Count > 0)
            {
                // B4 (§8.5.4 step 7): nothing of this entity applies.
                return new DataSyncMerge3Result(_local, [], new Dictionary<string, string>(_input.ChildMap), [], [], [],
                    [], outcome.MassDeletionCandidates, [], false);
            }

            _fields.AddRange(outcome.Fields);
            _warnings.AddRange(outcome.Warnings);
        }

        var merged = _local with
        {
            Name = _name,
            IgnoreCase = _ignoreCaseChanged ? _ignoreCase : _local.IgnoreCase,
            Settings = _settingsChanged ? _settings : _local.Settings,
            ChildrenLocal = _childrenLocalChanged ? _childrenLocal : _local.ChildrenLocal,
        };
        if (children is not null && outcome is not null)
        {
            merged = children.Kind switch
            {
                ChildListKind.Choices => merged with { Choices = ChildNode.ToChoices(outcome.Roots) },
                ChildListKind.Tags => merged with { Tags = ChildNode.ToTags(outcome.Roots) },
                _ => merged with { Nodes = ChildNode.ToNodes(outcome.Roots) },
            };
            merged = merged with { DefaultValue = MergeDefaultValue(children, merged) };
        }

        // What the service folds when it stores the result (F72): only adds, never a stored id.
        IReadOnlyDictionary<string, string> childMap = outcome?.ChildMap ?? new Dictionary<string, string>(_input.ChildMap);
        IReadOnlyList<string> added = outcome?.Added ?? [];
        if (CustomPropertyTypes.IsReference(merged.Type) && merged.IgnoreCase == true)
        {
            var fold = OptionFolding.Fold(merged, AllUuids(_local), ignoreCase: true);
            if (fold.Folds.Count > 0)
            {
                merged = children is null ? fold.Content : KeepClassColours(merged, fold.Content, children.Kind);
                childMap = childMap.ToDictionary(p => p.Key, p => fold.Aliases.GetValueOrDefault(p.Value, p.Value),
                    StringComparer.Ordinal);
                added = added.Where(id => !fold.Aliases.ContainsKey(id)).ToArray();
                foreach (var f in fold.Folds)
                {
                    var args = new Dictionary<string, string> { ["uuid"] = f.Uuid, ["intoLabel"] = f.IntoLabel };
                    if (f.Into is not null) args["into"] = f.Into;
                    _warnings.Add(new DataSyncPlanWarning(DataSyncWarningCode.OptionLabelConflict, null, args));
                }
            }
        }

        return new DataSyncMerge3Result(merged, _fields, childMap, added, outcome?.Removed ?? [], outcome?.Held ?? [],
            outcome?.Released ?? [], [], _warnings, false);
    }

    /// <summary>
    /// The local ids <see cref="Run"/> would remove if nothing used them: every candidate class's members and, for
    /// multilevel, their subtrees (§8.5.4); the merger reads their usage first.
    /// </summary>
    public IReadOnlyList<string> DeletionCandidates()
    {
        if (TypeChangedThere()) return [];
        MergeScalars();
        return PlanChildren()?.Engine.Plan() ?? [];
    }

    // ---- §8.5.6 -------------------------------------------------------------------------------

    /// <summary>
    /// A subtype that differs is never applied: the entity waits for a decision whenever the peer changed it. Only a
    /// three-way merge whose base has the peer's subtype says this device changed it alone; it then merges on.
    /// </summary>
    private bool TypeChangedThere() =>
        _local.Type != _remote.Type && !(_input.Mode3 == DataSyncMerge3Mode.ThreeWay && _base!.Type == _remote.Type);

    private DataSyncMerge3Result TypeChangeHeld()
    {
        var field = new DataSyncFieldOutcome(TypePath, DataSyncFieldResolution.TypeChangeHeld,
            _base is null ? null : TypeDisplay(_base.Type), TypeDisplay(_local.Type), TypeDisplay(_remote.Type),
            TypeDisplay(_local.Type));
        return new DataSyncMerge3Result(_local, [field], new Dictionary<string, string>(_input.ChildMap), [], [], [], [],
            [], [], true);
    }

    // ---- §8.5.2 -------------------------------------------------------------------------------

    private void MergeScalars()
    {
        _fields.Clear();
        if (_local.Type != _remote.Type)
        {
            // Only this device changed it since the base: kept, and the peer gets the TypeChange item.
            AddField(TypePath, DataSyncFieldResolution.KeptLocal, TypeDisplay(_base!.Type), TypeDisplay(_local.Type),
                TypeDisplay(_remote.Type), TypeDisplay(_local.Type));
        }

        _name = Scalar(NamePath, _base?.Name, _local.Name, _remote.Name, Text) ?? _local.Name;

        var bothReference = CustomPropertyTypes.IsReference(_local.Type) && CustomPropertyTypes.IsReference(_remote.Type);
        var localIgnoreCase = _local.IgnoreCase ?? false;
        _ignoreCase = localIgnoreCase;
        var localChildrenLocal = _input.LocalChildrenLocal || _local.ChildrenLocal;
        _childrenLocal = localChildrenLocal;
        if (bothReference)
        {
            var baseIgnoreCase = _base is not null && CustomPropertyTypes.IsReference(_base.Type)
                ? Flag(_base.IgnoreCase ?? false)
                : null;
            var ignoreCase = Scalar(IgnoreCasePath, baseIgnoreCase, Flag(localIgnoreCase), Flag(_remote.IgnoreCase ?? false),
                FlagDisplay);
            _ignoreCase = ignoreCase == "true";
            _ignoreCaseChanged = _ignoreCase != localIgnoreCase;

            var baseChildrenLocal = Flag(_input.BaseChildrenLocal || (_base?.ChildrenLocal ?? false));
            var childrenLocal = Scalar(ChildrenLocalPath, baseChildrenLocal, Flag(localChildrenLocal),
                Flag(_remote.ChildrenLocal), FlagDisplay);
            _childrenLocal = childrenLocal == "true";
            _childrenLocalChanged = _childrenLocal != localChildrenLocal;
            _childrenForcedNoBase = !_childrenLocal &&
                                    (localChildrenLocal || _input.BaseChildrenLocal || (_base?.ChildrenLocal ?? false));
        }

        var names = CustomPropertyTypes.SettingNames(_local.Type);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        _settingsChanged = false;
        foreach (var name in names)
        {
            var l = SettingValue(_local, name);
            var merged = Scalar($"{CustomPropertyJson.Settings}.{name}", _base is null ? null : SettingValue(_base, name),
                l, SettingValue(_remote, name), v => SettingDisplay(name, v));
            values[name] = merged;
            if (merged != l) _settingsChanged = true;
        }

        _settings = _settingsChanged ? BuildSettings(values) : _local.Settings;
    }

    /// <summary>
    /// One scalar path: the table of §8.5.2 in ThreeWay; the peer's value in FastForward; in NoBase a difference is
    /// concurrent; Convert merges <c>name</c> three-way against the base from before the type change (by the NoBase
    /// rule without one) and takes every other path from the peer (N7). Concurrent: a conflict that keeps the local
    /// value, or in Follow the peer's value unless the local one came from another device.
    /// </summary>
    private string? Scalar(string path, string? b, string? l, string? r, Func<string?, DataSyncDisplayValue?> display)
    {
        if (l == r) return l;
        var resolution = Decide(_input.Mode3, path, b, l, r);
        var result = ChildMerge3.TakesRemote(resolution) ? r : l;
        AddField(path, resolution, _base is null ? null : display(b), display(l), display(r), display(result));
        return result;
    }

    private DataSyncFieldResolution Decide(DataSyncMerge3Mode mode3, string path, string? b, string? l, string? r) =>
        mode3 switch
        {
            DataSyncMerge3Mode.FastForward => DataSyncFieldResolution.TookRemote,
            DataSyncMerge3Mode.Convert when path != NamePath => DataSyncFieldResolution.TookRemote,
            DataSyncMerge3Mode.ThreeWay => ThreeWay(b, l, r),
            DataSyncMerge3Mode.Convert when _base is not null => ThreeWay(b, l, r),
            _ => Concurrent(),
        };

    private DataSyncFieldResolution ThreeWay(string? b, string? l, string? r) =>
        l == b ? DataSyncFieldResolution.TookRemote : r == b ? DataSyncFieldResolution.KeptLocal : Concurrent();

    private DataSyncFieldResolution Concurrent() =>
        _input.Mode == DataSyncLinkMode.Follow && _input.LocalLastEditorIsSelf
            ? DataSyncFieldResolution.FollowTookRemote
            : DataSyncFieldResolution.Conflict;

    // ---- §8.5.4 -------------------------------------------------------------------------------

    private sealed record ChildPlan(ChildListKind Kind, ChildMergeRules Rules, ChildMerge3 Engine);

    /// <summary>
    /// The children's merge, or null when they stay as they are: "Sync the definition only" in the merged value, a
    /// type without options, or (this device changed the type) the two types keep different lists.
    /// </summary>
    private ChildPlan? PlanChildren()
    {
        if (_childrenLocal || CustomPropertyTypes.ChildListOf(_local.Type) is not { } list ||
            CustomPropertyTypes.ChildListOf(_remote.Type) != list) return null;

        var kind = list switch
        {
            CustomPropertyTypes.ChildList.Choices => ChildListKind.Choices,
            CustomPropertyTypes.ChildList.Tags => ChildListKind.Tags,
            _ => ChildListKind.Nodes,
        };
        var rules = _childrenForcedNoBase
            ? ChildMergeRules.NoBase
            : _input.Mode3 switch
            {
                DataSyncMerge3Mode.ThreeWay => ChildMergeRules.ThreeWay,
                DataSyncMerge3Mode.FastForward => ChildMergeRules.FastForward,
                _ => ChildMergeRules.NoBase,
            };
        if (_childrenForcedNoBase && !_warnings.Any(w => w.Code == DataSyncWarningCode.ChildrenLocalTurnedOff))
            _warnings.Add(new DataSyncPlanWarning(DataSyncWarningCode.ChildrenLocalTurnedOff, null, null));

        var settings = new ChildMergeSettings(kind, _ignoreCase == true, rules, _input.Mode, _input.LocalLastEditorIsSelf,
            _input.AppearanceWinner, _input.ChildMap, _input.LocalOverlay, _input.ChildDeletions, _policy);
        var baseRoots = rules != ChildMergeRules.NoBase && _base is not null ? ChildNode.Of(_base, kind) : null;
        var engine = new ChildMerge3(settings, ChildNode.Of(_local, kind), PublishedRoots(kind), ChildNode.Of(_remote, kind),
            baseRoots);
        return new ChildPlan(kind, rules, engine);
    }

    /// <summary>
    /// This device's list as it publishes it (§3.5 steps 1, 3 and 4): overlays and options without an id or a label out,
    /// then the reader's own per-option rules. Entity-level rules are not the children's business: the name, settings
    /// and default value are neutralized and the option limit lifted.
    /// </summary>
    private List<ChildNode> PublishedRoots(ChildListKind kind)
    {
        var hidden = _input.LocalOverlay.HiddenChildIds.ToHashSet(StringComparer.Ordinal);
        var probe = _local with { Name = "n", Settings = null, DefaultValue = [], ChildrenLocal = false };
        var stepped = CustomPropertyCodec.RemoveChildren(probe,
            (uuid, label) => string.IsNullOrEmpty(uuid) || label.Length == 0 || hidden.Contains(uuid),
            static (_, _, _) => { });
        var read = _codec.Read(CustomPropertyJson.Write(stepped),
            _codec.Limits with { MaxOptionsPerProperty = int.MaxValue });
        return read.Content is CustomPropertyContentV1 published ? ChildNode.Of(published, kind) : [];
    }

    // ---- defaultValue (§8.5.2, step 9) --------------------------------------------------------

    /// <summary>
    /// Compared as the classes the refs name as they end after the children's merge — the class keys the comparison
    /// form holds, of the very options each side names, so a rename is no change of the default — three-way like a
    /// scalar. The chosen side's refs are translated option by option through the merge; a ref whose option is gone
    /// here is dropped with <see cref="DataSyncWarningCode.DefaultValueRefDropped"/>.
    /// </summary>
    private IReadOnlyList<OptionRef> MergeDefaultValue(ChildPlan children, CustomPropertyContentV1 merged)
    {
        if (!CustomPropertyTypes.HasDefaultValue(_local.Type) || !CustomPropertyTypes.HasDefaultValue(_remote.Type))
            return KeepLocalDefault(children.Engine, merged);

        var engine = children.Engine;
        var l = TokenSet(_local.DefaultValue
            .Select(x => CustomPropertyRefs.Resolve(_local, x)?.Uuid)
            .Select(u => u is null ? null : engine.Token(engine.LocalTarget(u))));
        var r = TokenSet(Resolved(_remote).Select(u => engine.Token(engine.RemoteTarget(u))));
        string? b = null;
        if (children.Rules == ChildMergeRules.ThreeWay && _base is not null)
            b = TokenSet(Resolved(_base).Select(u => engine.Token(engine.BaseTarget(u))));

        if (l == r) return KeepLocalDefault(engine, merged);
        var mode3 = _input.Mode3 == DataSyncMerge3Mode.Convert
            ? DataSyncMerge3Mode.Convert
            : children.Rules switch
            {
                ChildMergeRules.ThreeWay => DataSyncMerge3Mode.ThreeWay,
                ChildMergeRules.FastForward => DataSyncMerge3Mode.FastForward,
                _ => DataSyncMerge3Mode.NoBase,
            };
        var resolution = Decide(mode3, DefaultValuePath, b, l, r);
        var result = ChildMerge3.TakesRemote(resolution)
            ? TranslateRemoteDefault(engine, merged)
            : KeepLocalDefault(engine, merged);
        AddField(DefaultValuePath, resolution, _base is null ? null : DefaultDisplay(_base.DefaultValue),
            DefaultDisplay(_local.DefaultValue), DefaultDisplay(_remote.DefaultValue), DefaultDisplay(result));
        return result;
    }

    private IReadOnlyList<OptionRef> TranslateRemoteDefault(ChildMerge3 engine, CustomPropertyContentV1 merged)
    {
        var refs = new List<OptionRef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var optionRef in _remote.DefaultValue)
        {
            var uuid = CustomPropertyRefs.Resolve(_remote, optionRef)?.Uuid;
            var target = uuid is null ? null : engine.RemoteTarget(uuid) is { Removed: false } node ? node.Uuid : null;
            if (target is null || CustomPropertyRefs.RefFor(merged, target) is not { } translated)
            {
                _warnings.Add(CustomPropertyCodec.Warning(DataSyncWarningCode.DefaultValueRefDropped, ("uuid", optionRef.Uuid)));
                continue;
            }

            if (seen.Add(target)) refs.Add(translated);
        }

        return Single(refs);
    }

    /// <summary>
    /// The local refs, less those whose option this merge removed. A ref is kept as it is while it still names the
    /// same, unchanged option; one to a renamed or moved option is rebuilt from the merged content. A ref that names no
    /// option here is kept as it is, unless it would name one after the merge (by id, label or path): it never meant
    /// that one.
    /// </summary>
    private IReadOnlyList<OptionRef> KeepLocalDefault(ChildMerge3 engine, CustomPropertyContentV1 merged)
    {
        var refs = new List<OptionRef>();
        foreach (var optionRef in _local.DefaultValue)
        {
            if (CustomPropertyRefs.Resolve(_local, optionRef)?.Uuid is not { } uuid)
            {
                if (CustomPropertyRefs.Resolve(merged, optionRef) is null) refs.Add(optionRef);
                else _warnings.Add(CustomPropertyCodec.Warning(DataSyncWarningCode.DefaultValueRefDropped, ("uuid", optionRef.Uuid)));
                continue;
            }

            if (engine.IsRemoved(uuid))
            {
                _warnings.Add(CustomPropertyCodec.Warning(DataSyncWarningCode.DefaultValueRefDropped, ("uuid", optionRef.Uuid)));
                continue;
            }

            var after = CustomPropertyRefs.RefFor(merged, uuid);
            var same = CustomPropertyRefs.Resolve(merged, optionRef)?.Uuid == uuid &&
                       Equals(CustomPropertyRefs.RefFor(_local, uuid), after);
            refs.Add(same || after is null ? optionRef : after);
        }

        return refs.SequenceEqual(_local.DefaultValue) ? _local.DefaultValue : refs;
    }

    private IReadOnlyList<OptionRef> Single(List<OptionRef> refs) =>
        _local.Type == PropertyType.SingleChoice && refs.Count > 1 ? refs.Take(1).ToArray() : refs;

    /// <summary>The options a peer content's (validated) refs name.</summary>
    private static IEnumerable<string> Resolved(CustomPropertyContentV1 content) =>
        content.DefaultValue.Select(x => CustomPropertyRefs.Resolve(content, x)?.Uuid).OfType<string>();

    private static string TokenSet(IEnumerable<string?> tokens) =>
        string.Join("\n", tokens.OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>
    /// Folding keeps every class (a duplicate folds into its class, and a folded node's children into the survivor's),
    /// but can change which member comes first in a multilevel class, and so the colour the class shows: each class of
    /// <paramref name="folded"/> gets the colour it had in <paramref name="merged"/> (§8.5.5).
    /// </summary>
    private static CustomPropertyContentV1 KeepClassColours(CustomPropertyContentV1 merged,
        CustomPropertyContentV1 folded, ChildListKind kind)
    {
        var colours = new Dictionary<string, string?>(StringComparer.Ordinal);
        VisitClasses(ChildNode.Of(merged, kind), "", kind,
            (path, rep) => colours.TryAdd(path, ChildMerge3.NormColor(rep.Color)));
        var roots = ChildNode.Of(folded, kind);
        var changed = false;
        VisitClasses(roots, "", kind, (path, rep) =>
        {
            if (!colours.TryGetValue(path, out var colour) || ChildMerge3.NormColor(rep.Color) == colour) return;
            rep.Color = colour;
            changed = true;
        });
        if (!changed) return folded;
        return kind switch
        {
            ChildListKind.Choices => folded with { Choices = ChildNode.ToChoices(roots) },
            ChildListKind.Tags => folded with { Tags = ChildNode.ToTags(roots) },
            _ => folded with { Nodes = ChildNode.ToNodes(roots) },
        };
    }

    /// <summary>Every label class under IgnoreCase (the folded content's), with its key path and representative.</summary>
    private static void VisitClasses(IEnumerable<ChildNode> level, string path, ChildListKind kind,
        Action<string, ChildNode> visit)
    {
        foreach (var members in level.GroupBy(n => kind == ChildListKind.Tags
                     ? DataSyncLabelKey.Fold(n.Group ?? "", true) + "\0" + DataSyncLabelKey.Fold(n.Label, true)
                     : DataSyncLabelKey.Fold(n.Label, true), StringComparer.Ordinal))
        {
            var classPath = path + "\u0001" + members.Key;
            visit(classPath, members.First());
            VisitClasses(members.SelectMany(m => m.Children), classPath, kind, visit);
        }
    }

    private void AddField(string path, DataSyncFieldResolution resolution, DataSyncDisplayValue? b,
        DataSyncDisplayValue? l, DataSyncDisplayValue? r, DataSyncDisplayValue? result) =>
        _fields.Add(new DataSyncFieldOutcome(path, resolution, b, l, r, result));

    private static HashSet<string> AllUuids(CustomPropertyContentV1 content)
    {
        var uuids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in content.Choices)
        {
            if (c.Uuid is not null) uuids.Add(c.Uuid);
        }

        foreach (var t in content.Tags)
        {
            if (t.Uuid is not null) uuids.Add(t.Uuid);
        }

        var stack = new Stack<CustomPropertyNodeV1>(content.Nodes);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Uuid is not null) uuids.Add(node.Uuid);
            foreach (var child in node.Children) stack.Push(child);
        }

        return uuids;
    }

    /// <summary>A setting's value as the type uses it (its default when absent), or null when the type has no such
    /// setting.</summary>
    private static string? SettingValue(CustomPropertyContentV1 content, string name)
    {
        if (!CustomPropertyTypes.SettingNames(content.Type).Contains(name)) return null;
        var settings = content.Settings;
        var defaults = CustomPropertyTypes.DefaultSettings(content.Type)!;
        return name switch
        {
            CustomPropertyJson.Precision => Number(settings?.Precision ?? defaults.Precision),
            CustomPropertyJson.ShowProgressBar => Flag(settings?.ShowProgressBar ?? defaults.ShowProgressBar),
            CustomPropertyJson.MaxValue => Number(settings?.MaxValue ?? defaults.MaxValue),
            CustomPropertyJson.Layout => settings?.Layout ?? defaults.Layout,
            CustomPropertyJson.ValueIsSingleton => Flag(settings?.ValueIsSingleton ?? defaults.ValueIsSingleton),
            _ => null,
        };
    }

    private static CustomPropertySettingsV1 BuildSettings(IReadOnlyDictionary<string, string?> values)
    {
        var settings = new CustomPropertySettingsV1();
        foreach (var (name, value) in values)
        {
            if (value is null) continue;
            settings = name switch
            {
                CustomPropertyJson.Precision => settings with { Precision = int.Parse(value, CultureInfo.InvariantCulture) },
                CustomPropertyJson.ShowProgressBar => settings with { ShowProgressBar = value == "true" },
                CustomPropertyJson.MaxValue => settings with { MaxValue = int.Parse(value, CultureInfo.InvariantCulture) },
                CustomPropertyJson.Layout => settings with { Layout = value },
                CustomPropertyJson.ValueIsSingleton => settings with { ValueIsSingleton = value == "true" },
                _ => settings,
            };
        }

        return settings;
    }

    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);
    private static string? Flag(bool? value) => value is null ? null : value.Value ? "true" : "false";
    private static DataSyncDisplayValue? Text(string? value) => value is null ? null : new DataSyncDisplayValue(value);
    private static DataSyncDisplayValue? FlagDisplay(string? value) => value is null ? null : new DataSyncDisplayValue(null, Flag: value == "true");

    private static DataSyncDisplayValue? TypeDisplay(PropertyType type) => Text(CustomPropertyTypes.NameOf(type));

    private static DataSyncDisplayValue? SettingDisplay(string name, string? value) => value is null
        ? null
        : name switch
        {
            CustomPropertyJson.Precision or CustomPropertyJson.MaxValue =>
                new DataSyncDisplayValue(value, Number: int.Parse(value, CultureInfo.InvariantCulture)),
            CustomPropertyJson.ShowProgressBar or CustomPropertyJson.ValueIsSingleton => FlagDisplay(value),
            _ => new DataSyncDisplayValue(value),
        };

    private static DataSyncDisplayValue? DefaultDisplay(IReadOnlyList<OptionRef> refs) => refs.Count == 0
        ? null
        : new DataSyncDisplayValue(string.Join(", ",
            refs.Select(r => r.Path is { } path ? string.Join(" / ", path) : r.Label ?? r.Name)));
}
