using System.Text.Json.Nodes;

namespace Bakabase.Modules.DataSync.Abstractions;

/// <summary>One definition as it exists on this device, already in canonical form.</summary>
public sealed record LocalEntity(
    string LocalKey,          // int id as invariant string ("12")
    string? Fingerprint,      // creation fingerprint for ID-reuse detection; null when the kind has none
    int Position,             // 0-based display order within the kind
    JsonObject Content,       // codec.Write(...) output
    bool Unreadable = false); // the stored row does not parse; Content is {name, type} only (§3.3)

/// <summary>
/// The per-kind adapter. It lives beside the service that owns the table and writes ONLY through that service.
/// </summary>
public interface IDataSyncKind
{
    IDataSyncKindCodec Codec { get; }

    /// <summary>localKeys == null → every entity of the kind.</summary>
    Task<IReadOnlyList<LocalEntity>> ReadAsync(IReadOnlyCollection<string>? localKeys, CancellationToken ct);

    /// <summary>Raw row snapshots used for undo (e.g. {name,type,options(raw string),order,createdAt}).</summary>
    Task<IReadOnlyDictionary<string, JsonObject>> CapturePreImageAsync(IReadOnlyCollection<string> localKeys,
        CancellationToken ct);

    /// <summary>Executes the batch's operations in order through the owning service, inside the caller's transaction.</summary>
    Task<ApplyBatchOutcome> ApplyAsync(ApplyBatch batch, CancellationToken ct);

    /// <summary>How much library data uses an entity and some of its children (undo safety, rename counts).</summary>
    Task<IReadOnlyDictionary<string, EntityUsage>> GetUsageAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> childIdsByLocalKey, CancellationToken ct);

    Task RestoreAsync(string localKey, JsonObject preImage, CancellationToken ct);
    Task DeleteAsync(string localKey, CancellationToken ct);

    /// <summary>Drop the service's in-memory cache (called after a rollback).</summary>
    void ResetCaches();

    /// <summary>Current local order of the kind (custom property: by (Order, Id)); kinds without order return Id order.</summary>
    Task<IReadOnlyList<string>> ReadOrderAsync(CancellationToken ct);

    /// <summary>
    /// Puts synced entities in the given order and keeps every other entity in its slot (§3.7).
    /// Writes only rows whose Order changes. No-op when !Codec.Descriptor.HasOrder.
    /// </summary>
    Task ApplyOrderAsync(IReadOnlyList<string> syncedLocalKeysInSharedOrder, CancellationToken ct);

    /// <summary>Changes the subtype through the owning service (custom property: ChangeType, after B0). Converts values.</summary>
    Task ChangeSubtypeAsync(string localKey, string subtype, CancellationToken ct);

    Task<DataSyncTypeChangePreview> PreviewSubtypeChangeAsync(string localKey, string subtype, CancellationToken ct);

    /// <summary>Hash of the raw stored row (custom property: Name, Type, Options string), for Refresh's fast path (§6.1).</summary>
    Task<IReadOnlyDictionary<string, string>> ReadRawHashesAsync(CancellationToken ct);
}

/// <summary>
/// ValueCount: stored values of the entity (custom property: CustomPropertyValues rows; extension group: 0).
/// ResourceCountByChildId: distinct resources whose value references each requested child id.
/// </summary>
public sealed record EntityUsage(int ValueCount, IReadOnlyDictionary<string, int> ResourceCountByChildId);

/// <param name="Samples">At most 20.</param>
public sealed record DataSyncTypeChangePreview(string FromSubtype, string ToSubtype, int ValueCount, int ChangedCount,
    int LossyCount, IReadOnlyList<DataSyncTypeChangeSample> Samples);

public sealed record DataSyncTypeChangeSample(string? From, string? To);
