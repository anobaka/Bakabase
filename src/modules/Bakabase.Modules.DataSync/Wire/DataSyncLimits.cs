namespace Bakabase.Modules.DataSync.Wire;

/// <summary>
/// Limits on peer input and on what a source serves (v3.1 §6.3 without the package-only members, plus the feed's
/// own). Init-only so a test may lower them; <see cref="Default"/> holds the specified values.
/// </summary>
public sealed record DataSyncLimits
{
    public static DataSyncLimits Default { get; } = new();

    // ---- v3.1 §6.3 -------------------------------------------------------------------------
    public int MaxJsonDepth { get; init; } = 64;
    public int MaxCustomProperties { get; init; } = 5_000;
    public int MaxExtensionGroups { get; init; } = 1_000;
    public int MaxOptionsPerProperty { get; init; } = 20_000;
    public int MaxKeysPerEntity { get; init; } = 64;
    public int MaxNameLength { get; init; } = 256;
    public int MaxMultilevelDepth { get; init; } = 16;
    public int MaxLabelLength { get; init; } = 1_024;
    public int MaxUuidLength { get; init; } = 128;
    public int MaxColorLength { get; init; } = 64;
    public int MaxExtensionLength { get; init; } = 32;
    public int MinPrecision { get; init; } = 0;
    public int MaxPrecision { get; init; } = 28;
    public int MinRatingMaxValue { get; init; } = 1;
    public int MaxRatingMaxValue { get; init; } = 100;

    // ---- continuous sync (§2.5) --------------------------------------------------------------
    public int MaxActorsPerVector { get; init; } = 256;
    public int MaxPageBytes { get; init; } = 3_584 << 10;          // 3.5 MiB, under MaxControlResponseBytes (F62)
    public int MaxRecordsPerPage { get; init; } = 500;

    /// <summary>A record's content fits a page with room left for the envelope, keys, vector and editor.</summary>
    public int MaxContentBytes => MaxPageBytes - (64 << 10);
    public long MaxSnapshotBytes { get; init; } = 64L << 20;       // per snapshot, in memory at the source
    public long MaxSnapshotBytesTotal { get; init; } = 256L << 20; // all snapshots of this source
    public long MaxStagedPullBytes { get; init; } = 96L << 20;     // one link's staged pull at the receiver
    public int MaxOrderKeyLength { get; init; } = 128;
    public int MaxEditorNameLength { get; init; } = 128;
}
