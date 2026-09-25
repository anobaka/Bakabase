using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// Version skew (§8.12; every commit to <c>main</c> is a beta). Beta policy (Q10): no compatibility shims. Records
/// of a newer schema are held (never partially applied) and re-merged after an upgrade; a newer comparison form is
/// drift, never a duplicate actor.
/// </summary>
public static class DataSyncVersionSkew
{
    /// <summary>
    /// The link state a peer's contract puts the link in, or null when both sides may sync: <c>PeerTooOld</c> when
    /// the peer names no contract (an older <c>/info</c>) or one below <see cref="DataSyncContract.MinimumPeerVersion"/>;
    /// <c>ThisTooOld</c> when this build is below the peer's minimum.
    /// </summary>
    public static DataSyncLinkState? CheckContract(int? peerContractVersion, int? peerMinimumContract)
    {
        if (peerContractVersion is not { } version || version < DataSyncContract.MinimumPeerVersion)
            return DataSyncLinkState.PeerTooOld;
        return peerMinimumContract is { } minimum && DataSyncContract.Version < minimum
            ? DataSyncLinkState.ThisTooOld
            : null;
    }

    /// <summary>
    /// Whether the source's comparison form differs by version for a kind (N5): the head's
    /// <c>ComparisonFormVersion</c> is missing or not this build's codec's. Row A2 then reads unequal forms under
    /// equal vectors as drift, never as a duplicate actor.
    /// </summary>
    public static bool IsComparisonFormMismatch(int? peerComparisonFormVersion, IDataSyncKindCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        return peerComparisonFormVersion != codec.ComparisonFormVersion;
    }

    /// <summary>This build's schema version per kind (<c>KindSchemaVersionsJson</c>).</summary>
    public static IReadOnlyDictionary<string, int> SchemaVersions(IEnumerable<IDataSyncKindCodec> codecs) =>
        codecs.ToDictionary(c => c.Descriptor.Kind, c => c.Descriptor.SchemaVersion, StringComparer.Ordinal);

    /// <summary>This build's comparison form version per kind (<c>ComparisonFormVersionsJson</c>).</summary>
    public static IReadOnlyDictionary<string, int> ComparisonFormVersions(IEnumerable<IDataSyncKindCodec> codecs) =>
        codecs.ToDictionary(c => c.Descriptor.Kind, c => c.ComparisonFormVersion, StringComparer.Ordinal);

    /// <summary>
    /// The kinds whose recorded version differs from this build's (a missing record counts as different):
    /// for schema versions, the <c>Held</c> pending records of these kinds are re-merged and a full reconciliation
    /// runs (§8.4 condition 6, §8.8); for comparison form versions, Refresh recomputes every <c>SharedHash</c> of the
    /// kind with no revision (§3.4, §6.1).
    /// </summary>
    public static IReadOnlyList<string> ChangedKinds(IReadOnlyDictionary<string, int> recorded,
        IReadOnlyDictionary<string, int> current)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(current);
        return current.Where(c => !recorded.TryGetValue(c.Key, out var v) || v != c.Value).Select(c => c.Key)
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
    }
}
