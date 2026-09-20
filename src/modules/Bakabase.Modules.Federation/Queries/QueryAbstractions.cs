using Bakabase.Modules.Federation.Contracts;

namespace Bakabase.Modules.Federation.Queries;

public sealed record QueryAccess(string NodeId, string LibraryEpoch, string GrantId, long GrantVersion);

/// <summary>Must verify current identity, sharing state and grant revision on EVERY call, including retries.</summary>
public interface IFederationQueryAccess
{
    Task<QueryAccess> ValidateAsync(string grantId, string expectedLibraryEpoch, CancellationToken cancellationToken);
    CancellationToken GetCancellationToken(string grantId) => CancellationToken.None;
}

public sealed record LocalResourceProjection(int ResourceId, string? EffectiveName, string? FileName,
    bool HasLocalPath, IReadOnlyList<int> SourceKinds);

public sealed record LocalLibraryCapture(string NodeId, string LibraryEpoch, string OwnerLabel,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt, IReadOnlyList<LocalResourceProjection> Resources);

/// <summary>Copies local data only. It must honor the budget during capture, not after allocating an unbounded list.</summary>
public interface ILocalLibraryReader
{
    Task<LocalLibraryCapture> CaptureAsync(CaptureBudget budget, CancellationToken cancellationToken);
}

public interface IPeerSearchClient
{
    Task<NodeQueryBlock> CreateAsync(NodeExportQuery query, CancellationToken cancellationToken);
    Task<NodeQueryBlock> ReadAsync(string snapshotId, string cursor, CancellationToken cancellationToken);
    Task ValidateAsync(string snapshotId, CancellationToken cancellationToken);
    Task ReleaseAsync(string snapshotId, CancellationToken cancellationToken);
}

public sealed record PeerSearchTarget(string NodeId, string LibraryEpoch, IPeerSearchClient Client);

/// <summary>Resolves ONLY explicitly configured direct peers (and optionally the local node).</summary>
public interface IPeerSearchTargetResolver
{
    Task<PeerSearchTarget> ResolveAsync(string nodeId, CancellationToken cancellationToken);
}

public sealed record FederationQueryLimits
{
    public int MaxTextLength { get; init; } = 256;
    public int MaxStringLength { get; init; } = 16_384;
    public int MaxNodes { get; init; } = 16;
    public int MaxPageSize { get; init; } = 200;
    public int MaxBlockSize { get; init; } = 256;
    public int BlockSize { get; init; } = 128;
    public long MaxBlockBytes { get; init; } = 2 * 1024 * 1024;
    public long MaxPageBytes { get; init; } = 4 * 1024 * 1024;
    public int MaxSnapshotsPerGrant { get; init; } = 2;
    public int MaxSnapshotCount { get; init; } = 32;
    public int MaxCoordinatorSessions { get; init; } = 16;
    public int MaxSessionsPerOwner { get; init; } = 2;
    public int MaxConcurrentPreparations { get; init; } = 4;
    public long MaxCaptureBytes { get; init; } = 128 * 1024 * 1024;
    // The 100k SQLite fixture produces about 52 MiB of accounted summary data.
    public long MaxSnapshotBytes { get; init; } = 64 * 1024 * 1024;
    public long MaxTotalSnapshotBytes { get; init; } = 128 * 1024 * 1024;
    public long MaxTotalCaptureBytes { get; init; } = 256 * 1024 * 1024;
    public long MaxCoordinatorBytes { get; init; } = 64 * 1024 * 1024;
    public TimeSpan CaptureTimeout { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan PreparationTimeout { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan PageTimeout { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan SnapshotTtl { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan SessionTtl { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Conservative accounting for simultaneously retained projection inputs/associations and parsing workspace.
/// This is not cumulative GC allocation: EF enumeration also allocates short-lived objects which are collected.
/// Benchmarks report both this charge and total managed allocations so they cannot be mistaken for each other.
/// </summary>
public sealed class CaptureBudget(long maxBytes, int maxStringLength)
{
    private long _bytes;
    public long Bytes => Interlocked.Read(ref _bytes);

    public void Charge(long bytes)
    {
        if (bytes < 0 || Interlocked.Add(ref _bytes, bytes) > maxBytes)
            throw new FederationQueryException("ScanBudgetExceeded", 503,
                "The local projection exceeds the capture workspace budget.");
    }

    public void ChargeString(string? value)
    {
        if (value?.Length > maxStringLength)
            throw new FederationQueryException("ScanBudgetExceeded", 503,
                "A local projection field exceeds the supported length.");
        Charge(32L + (value?.Length ?? 0) * 2L);
    }
}
