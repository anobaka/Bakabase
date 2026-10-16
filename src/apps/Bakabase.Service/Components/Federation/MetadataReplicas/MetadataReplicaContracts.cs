using System;
using System.Collections.Generic;
using System.Globalization;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Components.Federation.MetadataReplicas;

public sealed record ReplicaIdentity(string ServerId, string LibraryEpoch);

/// <summary>A durable position. Cursor belongs to the source, not to the replica's local row IDs.</summary>
public sealed record ReplicaCheckpoint(string Generation, long Sequence, string? Cursor, bool Complete, long CatalogRevision = 0);

public sealed record ReplicaResourceSort
{
    public string? Path { get; init; }
    public string? FileName { get; init; }
    public DateTime CreateDt { get; init; }
    public DateTime FileCreateDt { get; init; }
    public DateTime FileModifyDt { get; init; }
    public DateTime? PlayedAt { get; init; }
    public ResourceTag Tags { get; init; }
    public decimal? HealthScore { get; init; }
}

/// <summary>Preserves the exact CLR range type expected by the existing property descriptor.</summary>
public sealed record ReplicaRangeValue(string Type, string Value)
{
    public static ReplicaRangeValue From(IComparable value) => value switch
    {
        decimal v => new("decimal", v.ToString(CultureInfo.InvariantCulture)),
        int v => new("int32", v.ToString(CultureInfo.InvariantCulture)),
        long v => new("int64", v.ToString(CultureInfo.InvariantCulture)),
        float v => new("single", v.ToString("R", CultureInfo.InvariantCulture)),
        double v => new("double", v.ToString("R", CultureInfo.InvariantCulture)),
        DateTime v => new("datetime", v.ToString("O", CultureInfo.InvariantCulture)),
        DateTimeOffset v => new("datetimeoffset", v.ToString("O", CultureInfo.InvariantCulture)),
        TimeSpan v => new("timespan", v.Ticks.ToString(CultureInfo.InvariantCulture)),
        _ => throw new ArgumentException("Unsupported replica range type.")
    };

    public IComparable ToComparable() => Type switch
    {
        "decimal" => decimal.Parse(Value, CultureInfo.InvariantCulture),
        "int32" => int.Parse(Value, CultureInfo.InvariantCulture),
        "int64" => long.Parse(Value, CultureInfo.InvariantCulture),
        "single" => float.Parse(Value, CultureInfo.InvariantCulture),
        "double" => double.Parse(Value, CultureInfo.InvariantCulture),
        "datetime" => DateTime.Parse(Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        "datetimeoffset" => DateTimeOffset.Parse(Value, CultureInfo.InvariantCulture),
        "timespan" => TimeSpan.FromTicks(long.Parse(Value, CultureInfo.InvariantCulture)),
        _ => throw new ArgumentException("Unsupported replica range type.")
    };
}

public sealed record ReplicaPosting(PropertyPool Pool, int PropertyId, string? ValueKey,
    ReplicaRangeValue? RangeValue = null);

public sealed record ReplicaResource(int ResourceId, long Revision, string ResourceJson,
    ReplicaResourceSort Sort, IReadOnlyList<ReplicaPosting> Postings);

/// <summary>Kind/key are owner-local. JSON is an existing ordinary metadata DTO, not executable configuration.</summary>
public sealed record ReplicaCatalog(string Kind, string Key, string Json);
public sealed record ReplicaCatalogKey(string Kind, string Key);

public sealed record ReplicaBatch(ReplicaCheckpoint Expected, ReplicaCheckpoint Next,
    IReadOnlyList<ReplicaResource> Upserts, IReadOnlyList<int> Deletes,
    IReadOnlyList<ReplicaCatalog> CatalogUpserts, IReadOnlyList<ReplicaCatalogKey> CatalogDeletes);

public sealed record ReplicaSearchResult(IReadOnlyList<string> ResourceJson, long TotalCount,
    int Page, int PageSize, long Sequence);

public sealed record ReplicaStatus(ReplicaCheckpoint? Checkpoint, string? ActiveGeneration,
    long ResourceCount, long ActiveSequence, long StagedResourceCount = 0);

public sealed record ReplicaSyncState(bool Paused = false, DateTimeOffset? LastSyncedAtUtc = null,
    string? ErrorCode = null, string? Error = null, bool IsOffline = false, bool RefreshPending = false, bool CatchupPending = false);

public sealed record ReplicaResourceRow(int ResourceId, long Revision, string ResourceJson);

public sealed class ReplicaNotReadyException() : InvalidOperationException("This library has no completed local metadata replica.");
public sealed class ReplicaCheckpointConflictException() : InvalidOperationException("The metadata replica checkpoint changed; reload it before retrying.");
public sealed class ReplicaBudgetExceededException(string message) : InvalidOperationException(message);
