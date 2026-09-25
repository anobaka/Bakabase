using System.Globalization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Refs;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>
/// The first-contact review of one property against a local one (v3.1 §7.4): which incoming options map to which
/// local ones, the change rows a person can untick, and — for the accepted rows — the merged content. Diff and Merge
/// run the same plan, so a merge writes exactly what the rows showed.
/// </summary>
/// <remarks>
/// <para>
/// Options are matched as label classes under the <b>local</b> comparer (§3.4): by uuid first (anywhere in a
/// multilevel tree), then by class key among the mapped parent's classes, then among the adds already planned. A null
/// tag group and <c>""</c> are one, and so are exact duplicates with IgnoreCase off, so the review never adds a second
/// member to a class; a case-only difference under IgnoreCase is no change. A uuid-matched option whose new label is
/// another local class's key maps to that class (<c>OptionLabelConflict</c>) instead of being renamed. Colours are
/// never cleared, nodes are never moved (<c>NodeMoveIgnored</c>), nothing is ever removed.
/// </para>
/// <para>
/// Folding (§3.3.2) uses the <b>final</b> IgnoreCase: the service folds adds equivalent to a local option or to each
/// other when it stores them, so the merge folds first (<see cref="OptionFolding"/>, every local uuid preserved) and
/// the diff predicts it, with the condition under which it happens (<c>when</c>). Because matching already uses the
/// local comparer, a fold can only happen when the IgnoreCase change turns it on (<c>withIgnoreCaseChange</c>).
/// </para>
/// </remarks>
internal sealed class CustomPropertyReview
{
    private const string ChoicesPath = CustomPropertyJson.Choices;
    private const string TagsPath = CustomPropertyJson.Tags;
    private const string NodesPath = CustomPropertyJson.Nodes;

    private readonly CustomPropertyContentV1 _local;
    private readonly CustomPropertyContentV1 _incoming;
    private readonly bool _ignoreCase;
    private readonly List<ScalarChange> _scalars = [];
    private readonly List<DataSyncFieldChange> _childChanges = [];
    private readonly List<DataSyncPlanWarning> _warnings = [];
    private readonly List<ChoiceStep> _choiceSteps = [];
    private readonly List<TagStep> _tagSteps = [];
    private readonly List<NodeStep> _nodeSteps = [];
    private readonly List<AddEntry> _adds = [];
    private readonly HashSet<string> _takenUuids = new(StringComparer.Ordinal);
    private int _unchangedChildren;
    private int _localOnlyChildren;

    public CustomPropertyReview(CustomPropertyContentV1 local, CustomPropertyContentV1 incoming)
    {
        _local = local;
        _incoming = incoming;
        _ignoreCase = local.IgnoreCase == true;
        foreach (var uuid in AllUuids(local)) _takenUuids.Add(uuid);

        PlanScalars();
        if (!incoming.ChildrenLocal)
        {
            PlanChoices();
            PlanTags();
            PlanNodes();
        }
        else
        {
            _localOnlyChildren = CustomPropertyCodec.CountNodes(local.Nodes) + local.Choices.Count + local.Tags.Count;
        }

        PlanDefaultValue();
    }

    // ---- results ------------------------------------------------------------------------------

    public EntityDiff ToDiff()
    {
        var changes = _scalars.Select(s => s.Row).Concat(DefaultValueRows()).Concat(_childChanges).ToArray();
        var warnings = new List<DataSyncPlanWarning>(_warnings);
        warnings.AddRange(PredictFolds());
        return new EntityDiff(changes, warnings, _unchangedChildren, _localOnlyChildren);
    }

    public MergeResult Apply(IReadOnlySet<string> accepted)
    {
        var merged = _local;
        foreach (var scalar in _scalars)
        {
            if (accepted.Contains(scalar.Row.ChangeId)) merged = scalar.Apply(merged);
        }

        var created = new HashSet<AddEntry>(ReferenceEqualityComparer.Instance);
        merged = merged with
        {
            Choices = ApplyChoices(accepted, created),
            Tags = ApplyTags(accepted, created),
            Nodes = ApplyNodes(accepted, created),
        };

        var warnings = _warnings.Where(w => w.Code != DataSyncWarningCode.NodeMoveIgnored &&
                                            (w.ChangeId is null || accepted.Contains(w.ChangeId))).ToList();

        // Fold what the service will fold, with the final IgnoreCase.
        var finalIgnoreCase = merged.IgnoreCase == true;
        var fold = OptionFolding.Fold(merged, LocalUuids(), finalIgnoreCase);
        merged = fold.Content;
        warnings.AddRange(FoldWarnings(fold, null));

        var childIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (incomingUuid, target) in Targets())
        {
            var uuid = target.LocalUuid ?? (target.Add is { } add && created.Contains(add) ? add.Uuid : null);
            if (uuid is not null) childIdMap[incomingUuid] = fold.Aliases.GetValueOrDefault(uuid, uuid);
        }

        var addedChildIds = _adds.Where(a => created.Contains(a) && !fold.Aliases.ContainsKey(a.Uuid))
            .Select(a => a.Uuid).ToArray();

        if (accepted.Contains(CustomPropertyJson.DefaultValue) && _defaultChange is not null)
        {
            var refs = new List<OptionRef>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (incomingUuid, refUuid) in _defaultChange.Incoming)
            {
                if (!childIdMap.TryGetValue(incomingUuid, out var localUuid))
                {
                    warnings.Add(CustomPropertyCodec.Warning(DataSyncWarningCode.DefaultValueRefDropped, ("uuid", refUuid)));
                    continue;
                }

                if (seen.Add(localUuid) && CustomPropertyRefs.RefFor(merged, localUuid) is { } optionRef) refs.Add(optionRef);
            }

            if (refs.Count > 0) merged = merged with { DefaultValue = refs };
        }

        return new MergeResult(merged, childIdMap, addedChildIds, warnings);
    }

    // ---- scalars ----------------------------------------------------------------------------

    private sealed record ScalarChange(DataSyncFieldChange Row, Func<CustomPropertyContentV1, CustomPropertyContentV1> Apply);

    private void PlanScalars()
    {
        if (_local.Name != _incoming.Name)
        {
            AddScalar(CustomPropertyJson.Name, Text(_local.Name), Text(_incoming.Name), c => c with { Name = _incoming.Name });
        }

        if (CustomPropertyTypes.IsReference(_incoming.Type))
        {
            if ((_local.IgnoreCase ?? false) != (_incoming.IgnoreCase ?? false))
            {
                AddScalar(CustomPropertyJson.IgnoreCase, Flag(_local.IgnoreCase ?? false), Flag(_incoming.IgnoreCase ?? false),
                    c => c with { IgnoreCase = _incoming.IgnoreCase ?? false });
            }

            if (_local.ChildrenLocal != _incoming.ChildrenLocal)
            {
                AddScalar(CustomPropertyJson.ChildrenLocal, Flag(_local.ChildrenLocal), Flag(_incoming.ChildrenLocal),
                    c => c with { ChildrenLocal = _incoming.ChildrenLocal });
            }
        }

        var local = _local.Settings ?? new CustomPropertySettingsV1();
        var incoming = _incoming.Settings ?? new CustomPropertySettingsV1();
        foreach (var setting in CustomPropertyTypes.SettingNames(_incoming.Type))
        {
            var path = $"{CustomPropertyJson.Settings}.{setting}";
            switch (setting)
            {
                case CustomPropertyJson.Precision when incoming.Precision is { } v && v != local.Precision:
                    AddScalar(path, Number(local.Precision), Number(v), c => WithSettings(c, s => s with { Precision = v }));
                    break;
                case CustomPropertyJson.MaxValue when incoming.MaxValue is { } v && v != local.MaxValue:
                    AddScalar(path, Number(local.MaxValue), Number(v), c => WithSettings(c, s => s with { MaxValue = v }));
                    break;
                case CustomPropertyJson.ShowProgressBar when incoming.ShowProgressBar is { } v && v != local.ShowProgressBar:
                    AddScalar(path, Flag(local.ShowProgressBar), Flag(v),
                        c => WithSettings(c, s => s with { ShowProgressBar = v }));
                    break;
                case CustomPropertyJson.ValueIsSingleton when incoming.ValueIsSingleton is { } v && v != local.ValueIsSingleton:
                    AddScalar(path, Flag(local.ValueIsSingleton), Flag(v),
                        c => WithSettings(c, s => s with { ValueIsSingleton = v }));
                    break;
                case CustomPropertyJson.Layout when incoming.Layout is { } v && v != local.Layout:
                    AddScalar(path, Text(local.Layout), Text(v), c => WithSettings(c, s => s with { Layout = v }));
                    break;
            }
        }
    }

    private void AddScalar(string path, DataSyncDisplayValue? from, DataSyncDisplayValue? to,
        Func<CustomPropertyContentV1, CustomPropertyContentV1> apply) =>
        _scalars.Add(new ScalarChange(new DataSyncFieldChange(path, DataSyncFieldChangeKind.Set, path, from, to, null, null),
            apply));

    private static CustomPropertyContentV1 WithSettings(CustomPropertyContentV1 content,
        Func<CustomPropertySettingsV1, CustomPropertySettingsV1> change) =>
        content with { Settings = change(content.Settings ?? new CustomPropertySettingsV1()) };

    // ---- defaultValue -----------------------------------------------------------------------

    /// <param name="Incoming">(incoming option uuid, the ref's own uuid) per incoming ref, in order.</param>
    private sealed record DefaultChange(DataSyncFieldChange Row, IReadOnlyList<(string IncomingUuid, string RefUuid)> Incoming);

    private DefaultChange? _defaultChange;

    private void PlanDefaultValue()
    {
        if (_incoming.ChildrenLocal || !CustomPropertyTypes.HasDefaultValue(_incoming.Type) ||
            _incoming.DefaultValue.Count == 0) return;

        var incoming = new List<(string IncomingUuid, string RefUuid)>();
        foreach (var optionRef in _incoming.DefaultValue)
        {
            if (CustomPropertyRefs.Resolve(_incoming, optionRef) is { } resolved) incoming.Add((resolved.Uuid, optionRef.Uuid));
        }

        var targets = Targets().ToDictionary(t => t.IncomingUuid, t => t.Target, StringComparer.Ordinal);
        var translated = new List<string>();
        foreach (var (incomingUuid, _) in incoming)
        {
            if (!targets.TryGetValue(incomingUuid, out var target)) continue;
            var uuid = target.LocalUuid ?? target.Add?.Uuid;
            if (uuid is not null && !translated.Contains(uuid)) translated.Add(uuid);
        }

        if (_incoming.Type == PropertyType.SingleChoice && translated.Count > 1) translated.RemoveRange(1, translated.Count - 1);
        var local = _local.DefaultValue.Select(r => r.Uuid).Distinct().ToArray();
        if (translated.Count == 0 || translated.ToHashSet(StringComparer.Ordinal).SetEquals(local)) return;

        var row = new DataSyncFieldChange(CustomPropertyJson.DefaultValue, DataSyncFieldChangeKind.Set,
            CustomPropertyJson.DefaultValue, DefaultDisplay(_local.DefaultValue),
            DefaultDisplay(_incoming.DefaultValue), null, null);
        _defaultChange = new DefaultChange(row, incoming);
    }

    private IEnumerable<DataSyncFieldChange> DefaultValueRows() =>
        _defaultChange is null ? [] : [_defaultChange.Row];

    private static DataSyncDisplayValue? DefaultDisplay(IReadOnlyList<OptionRef> refs) => refs.Count == 0
        ? null
        : new DataSyncDisplayValue(string.Join(", ", refs.Select(r => r.Path is { } path ? string.Join(" / ", path) : r.Label)));

    // ---- children: shared -------------------------------------------------------------------

    private sealed class AddEntry
    {
        public required string ChangeId { get; init; }
        public required string IncomingUuid { get; init; }
        public required string Uuid { get; init; }
        public AddEntry? ParentAdd { get; init; }
        public CustomPropertyNodeV1? ParentLocal { get; init; }
        public Dictionary<string, AddEntry> ChildAddsByKey { get; } = new(StringComparer.Ordinal);
        public CustomPropertyChoiceV1? Choice { get; init; }
        public CustomPropertyTagV1? Tag { get; init; }
        public CustomPropertyNodeV1? Node { get; init; }
    }

    /// <summary>What an incoming option maps onto: a local option (by its uuid) or an add.</summary>
    private sealed record Target(string? LocalUuid, AddEntry? Add);

    private IEnumerable<(string IncomingUuid, Target Target)> Targets() =>
        _choiceSteps.Select(s => (s.IncomingUuid, s.Target))
            .Concat(_tagSteps.Select(s => (s.IncomingUuid, s.Target)))
            .Concat(_nodeSteps.Select(s => (s.IncomingUuid, s.Target)));

    private HashSet<string> LocalUuids() => AllUuids(_local).ToHashSet(StringComparer.Ordinal);

    private AddEntry NewAdd(string prefix, string path, string incomingUuid, DataSyncDisplayValue to, AddEntry? parentAdd,
        Func<string, AddEntry> create)
    {
        var changeId = $"{prefix}:add:{incomingUuid}";
        var uuid = incomingUuid;
        if (_takenUuids.Contains(uuid))
        {
            uuid = CustomPropertyUuids.Remap(incomingUuid, _takenUuids.Contains);
            _warnings.Add(new DataSyncPlanWarning(DataSyncWarningCode.OptionUuidRemapped, changeId,
                new Dictionary<string, string> { ["uuid"] = incomingUuid, ["newUuid"] = uuid }));
        }

        _takenUuids.Add(uuid);
        var add = create(uuid);
        _adds.Add(add);
        _childChanges.Add(new DataSyncFieldChange(changeId, DataSyncFieldChangeKind.AddChild, path, null, to,
            parentAdd?.ChangeId, null));
        return add;
    }

    private void Conflict(string incomingUuid, string intoUuid, string intoLabel) =>
        _warnings.Add(CustomPropertyCodec.Warning(DataSyncWarningCode.OptionLabelConflict,
            ("uuid", incomingUuid), ("into", intoUuid), ("intoLabel", intoLabel)));

    private void Rename(string prefix, string path, string incomingUuid, DataSyncDisplayValue from, DataSyncDisplayValue to) =>
        _childChanges.Add(new DataSyncFieldChange($"{prefix}:rename:{incomingUuid}", DataSyncFieldChangeKind.RenameChild,
            path, from, to, null, null));

    private void Recolor(string prefix, string path, string incomingUuid, DataSyncDisplayValue from, DataSyncDisplayValue to) =>
        _childChanges.Add(new DataSyncFieldChange($"{prefix}:recolor:{incomingUuid}", DataSyncFieldChangeKind.RecolorChild,
            path, from, to, null, null));

    /// <summary>Colours are never cleared in a review: only a non-empty incoming colour that differs is a change.</summary>
    private static bool Recolors(string? incoming, string? local) =>
        !string.IsNullOrEmpty(incoming) && !string.Equals(incoming, local, StringComparison.Ordinal);

    // ---- choices ----------------------------------------------------------------------------

    private sealed class ChoiceStep
    {
        public required string IncomingUuid { get; init; }
        public required Target Target { get; init; }
        public int LocalIndex { get; init; } = -1;
        public string? Rename { get; init; }
        public string? Recolor { get; init; }
    }

    private void PlanChoices()
    {
        var local = _local.Choices;
        var keys = local.Select(c => ChildClasses.KeyOf(c, _ignoreCase)).ToArray();
        var byUuid = new Dictionary<string, int>(StringComparer.Ordinal);
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < local.Count; i++)
        {
            if (local[i].Uuid is not { } uuid) continue;
            byUuid.TryAdd(uuid, i);
            byKey.TryAdd(keys[i], i);
        }

        var addsByKey = new Dictionary<string, AddEntry>(StringComparer.Ordinal);
        var targetedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in _incoming.Choices)
        {
            var uuid = c.Uuid!;
            var key = ChildClasses.KeyOf(c, _ignoreCase);
            var index = -1;
            string? rename = null;
            var mayRecolor = true;
            if (byUuid.TryGetValue(uuid, out var matched))
            {
                index = matched;
                if (keys[matched] != key)
                {
                    if (byKey.TryGetValue(key, out var other))
                    {
                        index = other;
                        mayRecolor = false;
                        Conflict(uuid, local[other].Uuid!, local[other].Label);
                    }
                    else
                    {
                        rename = c.Label;
                        Rename("choice", ChoicesPath, uuid, new DataSyncDisplayValue(local[matched].Label, local[matched].Color),
                            new DataSyncDisplayValue(c.Label, local[matched].Color));
                    }
                }
            }
            else if (byKey.TryGetValue(key, out var classMatch))
            {
                index = classMatch;
            }

            if (index < 0)
            {
                if (!addsByKey.TryGetValue(key, out var add))
                {
                    addsByKey[key] = add = NewAdd("choice", ChoicesPath, uuid, new DataSyncDisplayValue(c.Label, c.Color),
                        null, u => new AddEntry
                        {
                            ChangeId = $"choice:add:{uuid}", IncomingUuid = uuid, Uuid = u,
                            Choice = new CustomPropertyChoiceV1(u, c.Label, c.Color),
                        });
                }

                _choiceSteps.Add(new ChoiceStep { IncomingUuid = uuid, Target = new Target(null, add) });
                continue;
            }

            string? recolor = null;
            if (mayRecolor && Recolors(c.Color, local[index].Color))
            {
                recolor = c.Color;
                Recolor("choice", ChoicesPath, uuid, new DataSyncDisplayValue(rename ?? local[index].Label, local[index].Color),
                    new DataSyncDisplayValue(rename ?? local[index].Label, c.Color));
            }

            if (rename is null && recolor is null) _unchangedChildren++;
            targetedKeys.Add(keys[index]);
            _choiceSteps.Add(new ChoiceStep
            {
                IncomingUuid = uuid, Target = new Target(local[index].Uuid, null), LocalIndex = index, Rename = rename,
                Recolor = recolor,
            });
        }

        _localOnlyChildren += keys.Count(k => !targetedKeys.Contains(k));
    }

    private IReadOnlyList<CustomPropertyChoiceV1> ApplyChoices(IReadOnlySet<string> accepted, HashSet<AddEntry> created)
    {
        var choices = _local.Choices.ToList();
        foreach (var step in _choiceSteps)
        {
            if (step.LocalIndex < 0) continue;
            if (step.Rename is { } label && accepted.Contains($"choice:rename:{step.IncomingUuid}"))
                choices[step.LocalIndex] = choices[step.LocalIndex] with { Label = label };
            if (step.Recolor is { } color && accepted.Contains($"choice:recolor:{step.IncomingUuid}"))
                choices[step.LocalIndex] = choices[step.LocalIndex] with { Color = color };
        }

        foreach (var add in _adds.Where(a => a.Choice is not null && accepted.Contains(a.ChangeId)))
        {
            choices.Add(add.Choice!);
            created.Add(add);
        }

        return choices;
    }

    // ---- tags -------------------------------------------------------------------------------

    private sealed class TagStep
    {
        public required string IncomingUuid { get; init; }
        public required Target Target { get; init; }
        public int LocalIndex { get; init; } = -1;
        public CustomPropertyTagV1? Rename { get; init; }
        public string? Recolor { get; init; }
    }

    private void PlanTags()
    {
        var local = _local.Tags;
        var keys = local.Select(t => ChildClasses.KeyOf(t, _ignoreCase)).ToArray();
        var byUuid = new Dictionary<string, int>(StringComparer.Ordinal);
        var byKey = new Dictionary<DataSyncTagClassKey, int>();
        for (var i = 0; i < local.Count; i++)
        {
            if (local[i].Uuid is not { } uuid) continue;
            byUuid.TryAdd(uuid, i);
            byKey.TryAdd(keys[i], i);
        }

        var addsByKey = new Dictionary<DataSyncTagClassKey, AddEntry>();
        var targetedKeys = new HashSet<DataSyncTagClassKey>();
        foreach (var t in _incoming.Tags)
        {
            var uuid = t.Uuid!;
            var key = ChildClasses.KeyOf(t, _ignoreCase);
            var index = -1;
            CustomPropertyTagV1? rename = null;
            var mayRecolor = true;
            if (byUuid.TryGetValue(uuid, out var matched))
            {
                index = matched;
                if (keys[matched] != key)
                {
                    if (byKey.TryGetValue(key, out var other))
                    {
                        index = other;
                        mayRecolor = false;
                        Conflict(uuid, local[other].Uuid!, local[other].Name);
                    }
                    else
                    {
                        rename = local[matched] with { Group = t.Group, Name = t.Name };
                        Rename("tag", TagsPath, uuid, TagDisplay(local[matched]), TagDisplay(rename));
                    }
                }
            }
            else if (byKey.TryGetValue(key, out var classMatch))
            {
                index = classMatch;
            }

            if (index < 0)
            {
                if (!addsByKey.TryGetValue(key, out var add))
                {
                    addsByKey[key] = add = NewAdd("tag", TagsPath, uuid, TagDisplay(t), null, u => new AddEntry
                    {
                        ChangeId = $"tag:add:{uuid}", IncomingUuid = uuid, Uuid = u, Tag = t with { Uuid = u },
                    });
                }

                _tagSteps.Add(new TagStep { IncomingUuid = uuid, Target = new Target(null, add) });
                continue;
            }

            string? recolor = null;
            if (mayRecolor && Recolors(t.Color, local[index].Color))
            {
                recolor = t.Color;
                var shown = rename ?? local[index];
                Recolor("tag", TagsPath, uuid, TagDisplay(shown), TagDisplay(shown with { Color = t.Color }));
            }

            if (rename is null && recolor is null) _unchangedChildren++;
            targetedKeys.Add(keys[index]);
            _tagSteps.Add(new TagStep
            {
                IncomingUuid = uuid, Target = new Target(local[index].Uuid, null), LocalIndex = index, Rename = rename,
                Recolor = recolor,
            });
        }

        _localOnlyChildren += keys.Count(k => !targetedKeys.Contains(k));
    }

    private static DataSyncDisplayValue TagDisplay(CustomPropertyTagV1 tag) => new(tag.Name, tag.Color, tag.Group);

    private IReadOnlyList<CustomPropertyTagV1> ApplyTags(IReadOnlySet<string> accepted, HashSet<AddEntry> created)
    {
        var tags = _local.Tags.ToList();
        foreach (var step in _tagSteps)
        {
            if (step.LocalIndex < 0) continue;
            if (step.Rename is { } renamed && accepted.Contains($"tag:rename:{step.IncomingUuid}"))
                tags[step.LocalIndex] = tags[step.LocalIndex] with { Group = renamed.Group, Name = renamed.Name };
            if (step.Recolor is { } color && accepted.Contains($"tag:recolor:{step.IncomingUuid}"))
                tags[step.LocalIndex] = tags[step.LocalIndex] with { Color = color };
        }

        foreach (var add in _adds.Where(a => a.Tag is not null && accepted.Contains(a.ChangeId)))
        {
            tags.Add(add.Tag!);
            created.Add(add);
        }

        return tags;
    }

    // ---- multilevel nodes -------------------------------------------------------------------

    private sealed class NodeStep
    {
        public required string IncomingUuid { get; init; }
        public required Target Target { get; init; }
        public CustomPropertyNodeV1? LocalNode { get; init; }
        public string? Rename { get; init; }
        public string? Recolor { get; init; }
    }

    /// <summary>Where an incoming node's children go: a local class (with the node adds are appended under), the
    /// local roots, or an add.</summary>
    private sealed record NodeParent(NodeClass? Class, CustomPropertyNodeV1? Node, AddEntry? Add);

    private void PlanNodes()
    {
        var roots = ChildClasses.OfNodes(_local.Nodes, _ignoreCase);
        var classOf = new Dictionary<CustomPropertyNodeV1, NodeClass>(ReferenceEqualityComparer.Instance);
        var parentClassOf = new Dictionary<CustomPropertyNodeV1, NodeClass?>(ReferenceEqualityComparer.Instance);
        Index(roots, null);
        var byUuid = new Dictionary<string, CustomPropertyNodeV1>(StringComparer.Ordinal);
        IndexUuids(_local.Nodes);

        var rootAdds = new Dictionary<string, AddEntry>(StringComparer.Ordinal);
        var classAdds = new Dictionary<NodeClass, Dictionary<string, AddEntry>>(ReferenceEqualityComparer.Instance);
        var targetedClasses = new HashSet<NodeClass>(ReferenceEqualityComparer.Instance);
        Visit(_incoming.Nodes, new NodeParent(null, null, null), []);

        _localOnlyChildren += classOf.Count(p => !targetedClasses.Contains(p.Value));
        return;

        void Index(IReadOnlyList<NodeClass> level, NodeClass? parent)
        {
            foreach (var cls in level)
            {
                foreach (var member in cls.Members)
                {
                    classOf[member] = cls;
                    parentClassOf[member] = parent;
                }

                Index(cls.Children, cls);
            }
        }

        void IndexUuids(IReadOnlyList<CustomPropertyNodeV1> level)
        {
            foreach (var node in level)
            {
                if (node.Uuid is not null) byUuid.TryAdd(node.Uuid, node);
                IndexUuids(node.Children);
            }
        }

        void Visit(IReadOnlyList<CustomPropertyNodeV1> level, NodeParent parent, IReadOnlyList<string> parentPath)
        {
            foreach (var n in level)
            {
                var uuid = n.Uuid!;
                var key = ChildClasses.KeyOf(n, _ignoreCase);
                var path = parentPath.Append(n.Label).ToArray();
                NodeParent next;
                if (byUuid.TryGetValue(uuid, out var matched))
                {
                    var matchedParent = parentClassOf[matched];
                    if (parent.Add is not null || !ReferenceEquals(parent.Class, matchedParent))
                        _warnings.Add(CustomPropertyCodec.Warning(DataSyncWarningCode.NodeMoveIgnored, ("uuid", uuid)));

                    var matchedClass = classOf[matched];
                    var target = matched;
                    var targetClass = matchedClass;
                    string? rename = null;
                    var mayRecolor = true;
                    if (matchedClass.Key != key)
                    {
                        var siblings = matchedParent?.Children ?? roots;
                        if (siblings.FirstOrDefault(c => c.Key == key && Addressable(c) is not null) is { } other)
                        {
                            target = Addressable(other)!;
                            targetClass = other;
                            mayRecolor = false;
                            Conflict(uuid, target.Uuid!, target.Label);
                        }
                        else
                        {
                            rename = n.Label;
                            Rename("node", NodesPath, uuid, NodeDisplay(matched, null),
                                new DataSyncDisplayValue(n.Label, matched.Color, Path: path));
                        }
                    }

                    next = MatchTo(uuid, n, target, targetClass, rename, mayRecolor, path);
                }
                else if (parent.Add is null)
                {
                    var siblings = parent.Class?.Children ?? roots;
                    if (siblings.FirstOrDefault(c => c.Key == key && Addressable(c) is not null) is { } cls)
                    {
                        next = MatchTo(uuid, n, Addressable(cls)!, cls, null, true, path);
                    }
                    else
                    {
                        var scope = parent.Class is null
                            ? rootAdds
                            : classAdds.TryGetValue(parent.Class, out var s) ? s : classAdds[parent.Class] = new(StringComparer.Ordinal);
                        next = AddNode(uuid, n, key, scope, null, parent.Node, path);
                    }
                }
                else
                {
                    next = AddNode(uuid, n, key, parent.Add.ChildAddsByKey, parent.Add, null, path);
                }

                Visit(n.Children, next, path);
            }
        }

        NodeParent MatchTo(string uuid, CustomPropertyNodeV1 n, CustomPropertyNodeV1 target, NodeClass targetClass,
            string? rename, bool mayRecolor, IReadOnlyList<string> path)
        {
            string? recolor = null;
            if (mayRecolor && Recolors(n.Color, target.Color))
            {
                recolor = n.Color;
                Recolor("node", NodesPath, uuid, new DataSyncDisplayValue(rename ?? target.Label, target.Color, Path: path),
                    new DataSyncDisplayValue(rename ?? target.Label, n.Color, Path: path));
            }

            if (rename is null && recolor is null) _unchangedChildren++;
            targetedClasses.Add(targetClass);
            _nodeSteps.Add(new NodeStep
            {
                IncomingUuid = uuid, Target = new Target(target.Uuid, null), LocalNode = target, Rename = rename,
                Recolor = recolor,
            });
            return new NodeParent(targetClass, target, null);
        }

        NodeParent AddNode(string uuid, CustomPropertyNodeV1 n, string key, Dictionary<string, AddEntry> scope,
            AddEntry? parentAdd, CustomPropertyNodeV1? parentLocal, IReadOnlyList<string> path)
        {
            if (!scope.TryGetValue(key, out var add))
            {
                scope[key] = add = NewAdd("node", NodesPath, uuid, new DataSyncDisplayValue(n.Label, n.Color, Path: path),
                    parentAdd, u => new AddEntry
                    {
                        ChangeId = $"node:add:{uuid}", IncomingUuid = uuid, Uuid = u, ParentAdd = parentAdd,
                        ParentLocal = parentLocal, Node = new CustomPropertyNodeV1(u, n.Label, n.Color),
                    });
            }

            _nodeSteps.Add(new NodeStep { IncomingUuid = uuid, Target = new Target(null, add) });
            return new NodeParent(null, null, add);
        }
    }

    /// <summary>The class member an incoming option can map onto: the first one with a uuid.</summary>
    private static CustomPropertyNodeV1? Addressable(NodeClass cls) => cls.Members.FirstOrDefault(m => m.Uuid is not null);

    private static DataSyncDisplayValue NodeDisplay(CustomPropertyNodeV1 node, string? color) =>
        new(node.Label, color ?? node.Color);

    private IReadOnlyList<CustomPropertyNodeV1> ApplyNodes(IReadOnlySet<string> accepted, HashSet<AddEntry> created)
    {
        var work = new Dictionary<CustomPropertyNodeV1, WorkNode>(ReferenceEqualityComparer.Instance);
        var roots = _local.Nodes.Select(n => WorkNode.From(n, work)).ToList();
        foreach (var step in _nodeSteps)
        {
            if (step.LocalNode is null) continue;
            var node = work[step.LocalNode];
            if (step.Rename is { } label && accepted.Contains($"node:rename:{step.IncomingUuid}")) node.Label = label;
            if (step.Recolor is { } color && accepted.Contains($"node:recolor:{step.IncomingUuid}")) node.Color = color;
        }

        var addedWork = new Dictionary<AddEntry, WorkNode>(ReferenceEqualityComparer.Instance);
        foreach (var add in _adds.Where(a => a.Node is not null))
        {
            if (!accepted.Contains(add.ChangeId)) continue;
            List<WorkNode> siblings;
            if (add.ParentAdd is { } parentAdd)
            {
                // A child add whose parent add was not accepted has nowhere to go (Resolve excludes it anyway).
                if (!addedWork.TryGetValue(parentAdd, out var parentWork)) continue;
                siblings = parentWork.Children;
            }
            else
            {
                siblings = add.ParentLocal is { } parentLocal ? work[parentLocal].Children : roots;
            }

            var node = WorkNode.From(add.Node!, null);
            siblings.Add(node);
            addedWork[add] = node;
            created.Add(add);
        }

        return roots.Select(r => r.ToNode()).ToArray();
    }

    private sealed class WorkNode
    {
        public required CustomPropertyNodeV1 Source { get; init; }
        public required string Label { get; set; }
        public string? Color { get; set; }
        public required List<WorkNode> Children { get; init; }

        public static WorkNode From(CustomPropertyNodeV1 node, Dictionary<CustomPropertyNodeV1, WorkNode>? index)
        {
            var work = new WorkNode
            {
                Source = node, Label = node.Label, Color = node.Color,
                Children = node.Children.Select(c => From(c, index)).ToList(),
            };
            if (index is not null) index[node] = work;
            return work;
        }

        public CustomPropertyNodeV1 ToNode() =>
            Source with { Label = Label, Color = Color, Children = Children.Select(c => c.ToNode()).ToArray() };
    }

    // ---- folding ----------------------------------------------------------------------------

    /// <summary>
    /// The folds the service will make when every add is accepted (v3.1 §3.3.2): with no IgnoreCase change they happen
    /// <c>always</c> (or never); with a change, only in the state where the final IgnoreCase is true.
    /// </summary>
    private IEnumerable<DataSyncPlanWarning> PredictFolds()
    {
        var allAdds = _adds.Select(a => a.ChangeId).ToHashSet(StringComparer.Ordinal);
        var merged = ApplyAddsOnly(allAdds);
        var change = _scalars.Any(s => s.Row.ChangeId == CustomPropertyJson.IgnoreCase);
        string when;
        if (!change)
        {
            if (_local.IgnoreCase != true) return [];
            when = "always";
        }
        else
        {
            when = _incoming.IgnoreCase == true ? "withIgnoreCaseChange" : "withoutIgnoreCaseChange";
        }

        return FoldWarnings(OptionFolding.Fold(merged, LocalUuids(), ignoreCase: true), when);
    }

    private CustomPropertyContentV1 ApplyAddsOnly(IReadOnlySet<string> accepted)
    {
        var created = new HashSet<AddEntry>(ReferenceEqualityComparer.Instance);
        return _local with
        {
            Type = _incoming.Type,
            Choices = ApplyChoices(accepted, created),
            Tags = ApplyTags(accepted, created),
            Nodes = ApplyNodes(accepted, created),
        };
    }

    private IEnumerable<DataSyncPlanWarning> FoldWarnings(OptionFoldResult fold, string? when)
    {
        var addsByUuid = _adds.ToDictionary(a => a.Uuid, StringComparer.Ordinal);
        foreach (var f in fold.Folds)
        {
            var add = addsByUuid.GetValueOrDefault(f.Uuid);
            var args = new Dictionary<string, string>
            {
                ["uuid"] = add?.IncomingUuid ?? f.Uuid,
                ["intoLabel"] = f.IntoLabel,
            };
            if (f.Into is not null) args["into"] = f.Into;
            if (when is not null) args["when"] = when;
            yield return new DataSyncPlanWarning(DataSyncWarningCode.OptionLabelConflict, add?.ChangeId, args);
        }
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static IEnumerable<string> AllUuids(CustomPropertyContentV1 content)
    {
        foreach (var c in content.Choices)
        {
            if (c.Uuid is not null) yield return c.Uuid;
        }

        foreach (var t in content.Tags)
        {
            if (t.Uuid is not null) yield return t.Uuid;
        }

        var stack = new Stack<CustomPropertyNodeV1>(content.Nodes);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Uuid is not null) yield return node.Uuid;
            foreach (var child in node.Children) stack.Push(child);
        }
    }

    private static DataSyncDisplayValue? Text(string? text) => text is null ? null : new DataSyncDisplayValue(text);
    private static DataSyncDisplayValue Flag(bool? flag) => new(null, Flag: flag);
    private static DataSyncDisplayValue Number(int? number) => new(number?.ToString(CultureInfo.InvariantCulture), Number: number);
}
