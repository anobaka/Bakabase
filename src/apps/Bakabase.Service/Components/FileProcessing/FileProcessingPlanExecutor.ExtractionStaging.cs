using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingPlanExecutor
{
    private async Task<ArchiveExtractionResult> ExtractWithSpaceBudgetAsync(ArchiveExtractionRequest request,
        Operation operation, Func<Task> save, Func<CancellationToken, Task<ArchiveExtractionResult>> extract,
        Func<string, Task>? reportStage, CancellationToken ct)
    {
        if (writeGuard == null) return await extract(ct);
        long? bytes = operation.ExpandedBytes;
        if (bytes is null or < 0)
        {
            bytes = await extraction.TryGetExpandedBytesAsync(request, ct);
            if (bytes is >= 0)
            {
                operation.ExpandedBytes = bytes;
                await save();
            }
        }
        if (bytes is >= 0)
        {
            if (writeGuard is IStorageWriteReservationGuard reservations)
                return await reservations.RunReservedAsync([new(request.Directory, bytes.Value)], StorageWriteScope.Current,
                    extract, reportStage == null ? null : () => reportStage("waitingForStorageCapacity"), ct);
            await writeGuard.CheckAllocationAsync(request.Directory, bytes.Value, StorageWriteScope.Current, ct);
        }
        return await writeGuard.RunAsync([request.Directory], StorageWriteScope.Current, extract, ct);
    }

    private static bool ExclusiveExtractionStaging(string pending, string destination, Operation operation, State state, string checkpoint)
    {
        if (operation.Completed || operation.OutputManifest != null || !IsInside(Path.Combine(checkpoint, "outputs"), pending) ||
            !FileProcessingFiles.PathComparer.Equals(destination, operation.OutputDirectory)) return false;
        bool Overlaps(string path) => FileProcessingFiles.PathComparer.Equals(pending, path) || IsInside(pending, path) || IsInside(path, pending);
        return !state.Operations.Values.Where(o => !ReferenceEquals(o, operation) && o.OutputDirectory != null)
                   .Any(o => FileProcessingFiles.PathComparer.Equals(destination, o.OutputDirectory) ||
                             Overlaps(o.OutputDirectory!) || Overlaps(ExtractionStagingDirectory(o.OutputDirectory!, o, checkpoint))) &&
               !state.BoundaryResults.Values.Any(b => Overlaps(b.Directory)) &&
               !state.Active.Any(a => Overlaps(a.Path)) && !state.ActiveDirectories.Any(a => Overlaps(a.Path)) &&
               !state.FinalFiles.Any(Overlaps);
    }

    internal static string ExtractionStagingDirectory(string destination, Operation operation, string checkpoint)
    {
        var suffix = operation.ExtractionStagingId is { } id ? $".pending.{id:N}" : ".pending";
        return FileProcessingFiles.Within(destination + suffix, checkpoint);
    }

    private static async Task<string> PrepareExtractionStagingAsync(string destination, Operation operation, State state,
        string checkpoint, Func<Task> save)
    {
        var pending = ExtractionStagingDirectory(destination, operation, checkpoint);
        if (operation.OutputManifest != null)
            throw new InvalidDataException("The verified extraction staging directory is missing. Its receipt was retained.");

        if (!ExclusiveExtractionStaging(pending, destination, operation, state, checkpoint))
            throw new InvalidDataException("The archive staging directory overlaps another checkpoint. Its contents were retained.");
        // Reopened claims require the durable directory identity. A replacement, link,
        // mount or delete-on-close marker must never fall through to legacy recursive reset.
        if (operation.UnverifiedExtractionOwner is {} owner)
        {
            if (!owner.WriterStarted)
            {
                // Admission alone cannot authorize deleting even one file. An unchanged empty
                // directory can serve the retry; anything else stays at its original path.
                using var empty = DeferredEmptyDirectoryTree.PinEmptyExtraction(pending, owner);
                if (empty != null) return pending;
            }
            else DeferredEmptyDirectoryTree.DiscardClaimedExtraction(pending, owner);
            // Never recursively reset this old name after checking it: it may have
            // been replaced or recreated even when its original tree was removed.
        }
        else if (!FileProcessingFiles.ContainsOnlySmbDeleteMarkers(pending))
        {
            try
            {
                ResetOwnedDirectory(pending, checkpoint);
                return pending;
            }
            catch (IOException) when (FileProcessingFiles.ContainsOnlySmbDeleteMarkers(pending))
            {
                // An open server-side handle can leave a marker during the recursive delete.
            }
        }

        // Only the new directory may be passed to the writer. Persist that exact
        // path before extraction so a verified manifest can still be published after a crash.
        var id = Guid.NewGuid();
        var fresh = FileProcessingFiles.Within($"{destination}.pending.{id:N}", checkpoint);
        DeferredEmptyDirectoryTree.CreateFreshExtractionDirectory(fresh);
        operation.ExtractionStagingId = id;
        operation.UnverifiedExtractionOwner = null;
        await save();
        return fresh;
    }
}
