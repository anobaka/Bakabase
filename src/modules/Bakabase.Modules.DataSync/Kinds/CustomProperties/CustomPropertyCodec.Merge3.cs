using Bakabase.Modules.DataSync.Abstractions;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

public sealed partial class CustomPropertyCodec
{
    /// <summary>
    /// §8.5.4: every local id <c>Merge3</c> could remove because the peer deleted it — the members of every candidate
    /// class and, for multilevel, their whole subtrees, since a candidate's usage is the sum over all of them (a missing
    /// usage entry counts as in use). Only publishable children are ever candidates, never an overlay child.
    /// </summary>
    /// <remarks>
    /// The input carries no link mode, and a conflict on <c>ignoreCase</c> or a node's parent decides the classes and
    /// the subtrees; the ids of both readings (two-way, and Follow taking the peer's values) are returned, which only
    /// asks for more usage than a merge reads.
    /// </remarks>
    protected override IReadOnlyList<string> ChildDeletionCandidates(CustomPropertyContentV1? baseContent,
        CustomPropertyContentV1 local, CustomPropertyContentV1 remote, DataSyncChildCandidatesInput input)
    {
        // With "Sync the definition only" on any side the children merge by the NoBase rules or not at all (§3.6).
        if (input.ChildrenLocalAnySide) return [];
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mode in (DataSyncLinkMode[])[DataSyncLinkMode.TwoWay, DataSyncLinkMode.Follow])
        {
            var merge3 = new DataSyncMerge3Input(input.Base, input.Local, input.LocalOverlay, input.Remote, input.Mode3,
                input.ChildMap, LocalChildrenLocal: false, BaseChildrenLocal: false, mode, LocalLastEditorIsSelf: true,
                DataSyncMergeSide.Local, new Dictionary<string, int>(), DataSyncChildDeletionMode.Normal);
            foreach (var id in new CustomPropertyMerge(this, baseContent, local, remote, merge3, _policy).DeletionCandidates())
            {
                if (seen.Add(id)) ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// §8.5: the field-level merge of one custom property. A subtype the peer changed is never applied
    /// (<c>TypeChanged</c>, §8.5.6); otherwise <c>name</c>, <c>ignoreCase</c>, <c>childrenLocal</c> and the type's
    /// settings merge as scalars (§8.5.2), the children as label classes (§8.5.4, colours §8.5.5) and
    /// <c>defaultValue</c> through the class map. <c>Convert</c> is phase two of a type change (N7): only <c>name</c>
    /// merges three-way, every other scalar takes the peer's value and the children are unioned. When B4 trips (§8.5.4
    /// step 7) <c>Merged</c> is the local content and nothing else applies. See <see cref="CustomPropertyMerge"/> and
    /// <see cref="ChildMerge3"/>.
    /// </summary>
    protected override DataSyncMerge3Result Merge3(CustomPropertyContentV1? baseContent, CustomPropertyContentV1 local,
        CustomPropertyContentV1 remote, DataSyncMerge3Input input) =>
        new CustomPropertyMerge(this, baseContent, local, remote, input, _policy).Run();
}
