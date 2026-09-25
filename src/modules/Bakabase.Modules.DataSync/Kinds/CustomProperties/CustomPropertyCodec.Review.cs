using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Planning;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

public sealed partial class CustomPropertyCodec
{
    /// <summary>
    /// v3.1 §2.2: Clash = the same name ignoring case and surrounding whitespace, another type; Similar = that, the same
    /// type; Exact = an ordinal-equal name, the same type; Identical = Exact with equal comparison forms (§3.4), that
    /// is equal content except child ids, child order and duplicates. Custom properties never link automatically, so
    /// Identical is informational.
    /// </summary>
    public override DataSyncNaturalMatch MatchNatural(CustomPropertyContentV1 incoming, CustomPropertyContentV1 local)
    {
        if (!string.Equals(incoming.Name.Trim(), local.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            return DataSyncNaturalMatch.None;
        if (incoming.Type != local.Type) return DataSyncNaturalMatch.Clash;
        if (!string.Equals(incoming.Name, local.Name, StringComparison.Ordinal)) return DataSyncNaturalMatch.Similar;
        var incomingForm = CanonicalJson.Serialize(ComparisonForm(incoming, null, incoming.ChildrenLocal));
        var localForm = CanonicalJson.Serialize(ComparisonForm(local, null, local.ChildrenLocal));
        return incomingForm == localForm ? DataSyncNaturalMatch.Identical : DataSyncNaturalMatch.Exact;
    }

    /// <summary>
    /// v3.1 §7.4: every change that brings <paramref name="local"/> towards <paramref name="incoming"/>, never a
    /// removal; see <see cref="CustomPropertyReview"/> for the matching rules. The local side is ReadLocal content.
    /// </summary>
    public override EntityDiff Diff(CustomPropertyContentV1 local, CustomPropertyContentV1 incoming) =>
        new CustomPropertyReview(local, incoming).ToDiff();

    /// <summary>
    /// v3.1 §7.7: the local content with the accepted changes applied. Every local option stays, in local order; one
    /// changes only through an accepted rename or recolour; accepted adds are appended (a node under its mapped
    /// parent) and folded as the service will fold them.
    /// </summary>
    public override MergeResult Merge(CustomPropertyContentV1 local, CustomPropertyContentV1 incoming,
        IReadOnlySet<string> acceptedChangeIds) =>
        new CustomPropertyReview(local, incoming).Apply(acceptedChangeIds);

    /// <summary>
    /// A create on this device (v3.1 §3.3.2, §8.4 row N3): the incoming content, renamed when asked, folded as a fresh
    /// <c>AddRange</c> folds it (no preserved ids, the incoming IgnoreCase). A folded option is reported as
    /// <see cref="DataSyncWarningCode.OptionLabelConflict"/> (<c>when</c> = always) and mapped to its survivor in
    /// <see cref="MergeResult.ChildIdMap"/>; <see cref="MergeResult.AddedChildIds"/> lists every option created.
    /// </summary>
    public override MergeResult PrepareCreate(CustomPropertyContentV1 incoming, string? nameOverride)
    {
        var content = nameOverride is null ? incoming : incoming with { Name = nameOverride };
        var fold = OptionFolding.Fold(content);
        var childIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var child in ChildrenOf(content)) childIdMap.TryAdd(child.Id, fold.Aliases.GetValueOrDefault(child.Id, child.Id));
        var added = ChildrenOf(fold.Content).Select(c => c.Id).ToArray();
        var warnings = fold.Folds.Select(f =>
        {
            var args = new Dictionary<string, string> { ["uuid"] = f.Uuid, ["intoLabel"] = f.IntoLabel, ["when"] = "always" };
            if (f.Into is not null) args["into"] = f.Into;
            return new DataSyncPlanWarning(DataSyncWarningCode.OptionLabelConflict, null, args);
        }).ToArray();
        return new MergeResult(fold.Content, childIdMap, added, warnings);
    }
}
