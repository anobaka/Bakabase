using Bakabase.Modules.DataSync.Abstractions;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

public sealed partial class CustomPropertyCodec
{
    // The continuous field-level merge (§8.5, ChildMerge3) is package B's next step; the classes it merges are
    // ChildClasses, and the form it must agree with is ComparisonForm (§3.4).

    protected override IReadOnlyList<string> ChildDeletionCandidates(CustomPropertyContentV1? baseContent,
        CustomPropertyContentV1 local, CustomPropertyContentV1 remote, DataSyncChildCandidatesInput input) =>
        throw new NotImplementedException("customProperty: ChildDeletionCandidates lands with ChildMerge3 (§8.5.4).");

    protected override DataSyncMerge3Result Merge3(CustomPropertyContentV1? baseContent, CustomPropertyContentV1 local,
        CustomPropertyContentV1 remote, DataSyncMerge3Input input) =>
        throw new NotImplementedException("customProperty: Merge3 lands with ChildMerge3 (§8.5).");
}
