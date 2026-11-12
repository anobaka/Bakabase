using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Compression;

public partial class CompressedFileService
{
    /// <summary>For one selected archive operation, resolve a bounded structural ZIP hint.
    /// This is not filesystem discovery and does not search, copy or expand its payload.</summary>
    private static async Task<ArchiveOpenOptions?> ResolveAutomaticOpeningAsync(string path,
        ArchiveOpenOptions? opening, CancellationToken ct)
    {
        if (opening?.Mode != null || opening?.TypeArgument != null || opening?.ZipStructure != null)
            return opening; // Explicit parser/format decisions remain authoritative.
        ct.ThrowIfCancellationRequested();
        var before = new FileInfo(path);
        if (!before.Exists) return opening;
        if (before.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Archive structure discovery requires a regular file.");
        var length = before.Length; var modified = before.LastWriteTimeUtc;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(ArchiveFormatProbeLimits.PerCandidate);
        ZipStructureDescriptor? structure;
        try { structure = await BoundedZipStructureProbe.FindStructureAsync(path, length, stop.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ArchiveFormatProbeIncompleteException("Archive structure discovery exceeded its time limit.",
                ArchiveFormatProbeFailureCodes.TimedOut);
        }
        var after = new FileInfo(path);
        if (!after.Exists || after.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            after.Length != length || after.LastWriteTimeUtc != modified)
            throw new InvalidDataException("The archive input changed during structure discovery.");
        if (structure == null) return opening;
        var resolved = structure.ZipOffset < BoundedZipStructureProbe.MaximumPrefixBytes
            ? ArchiveOpenOptions.VerifiedZipPrefix(structure.ZipOffset)
            : ArchiveOpenOptions.VerifiedZipStructure(structure);
        resolved = ArchiveOpenOptions.WithZipFileNameCodePage(resolved, opening?.ZipFileNameCodePage)!;
        return ArchiveOpenOptions.WithZipPasswordCodePage(resolved, opening?.ZipPasswordCodePage);
    }
}
