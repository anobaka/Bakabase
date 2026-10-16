using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Services;

/// <summary>Bounded, consistent reads of the existing normalized resource index.</summary>
public interface IResourceSearchIndexExportService
{
    const int MaximumResourceCount = 100;
    const int MaximumEncodedBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Waits for queued updates, then copies the requested resources' postings under the
    /// index mutation gate. An unavailable index or oversized batch is an error, never a
    /// partial result. IDs absent from the index are omitted. Inside ResourceMetadataReadScope,
    /// builds detached postings from bounded persisted rows instead; it neither relies on event
    /// delivery nor reads/changes the global index.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<ResourceSearchIndexEntry>>> ReadIndexEntriesAsync(
        int[] resourceIds, CancellationToken ct = default);
}

public sealed class ResourceSearchIndexExportBudgetException()
    : InvalidOperationException("The resource search index export exceeds the byte budget.");
