using Bakabase.Modules.DataSync.Abstractions;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

public sealed partial class CustomPropertyCodec
{
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
