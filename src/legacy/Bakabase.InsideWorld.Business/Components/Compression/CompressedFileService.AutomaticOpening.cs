using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Compression;

public partial class CompressedFileService
{
    /// <summary>For one selected archive operation, resolve a bounded structural opening hint.
    /// This is not filesystem discovery and does not search, copy or expand its payload.</summary>
    private static async Task<ArchiveOpenOptions?> ResolveAutomaticOpeningAsync(string path,
        ArchiveOpenOptions? opening, CancellationToken ct)
    {
        path = opening?.InspectionPath(path) ?? path;
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
        ArchiveOpenOptions? resolved;
        try { resolved = await FindStructuralOpeningAsync(path, length, stop.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ArchiveFormatProbeIncompleteException("Archive structure discovery exceeded its time limit.",
                ArchiveFormatProbeFailureCodes.TimedOut);
        }
        var after = new FileInfo(path);
        if (!after.Exists || after.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            after.Length != length || after.LastWriteTimeUtc != modified)
            throw new InvalidDataException("The archive input changed during structure discovery.");
        if (resolved == null) return opening;
        if (resolved.ExpectedPrefixType == "rar5" &&
            (opening?.ZipFileNameCodePage != null || opening?.ZipPasswordCodePage != null)) return opening;
        resolved = ArchiveOpenOptions.WithZipFileNameCodePage(resolved, opening?.ZipFileNameCodePage)!;
        return ArchiveOpenOptions.WithZipPasswordCodePage(resolved, opening?.ZipPasswordCodePage);
    }

    private static async Task<ArchiveOpenOptions?> FindStructuralOpeningAsync(string path, long length, CancellationToken ct)
    {
        var zip = await BoundedZipStructureProbe.FindStructureAsync(path, length, ct);
        if (zip != null)
            return zip.ZipOffset < BoundedZipStructureProbe.MaximumPrefixBytes
                ? ArchiveOpenOptions.VerifiedZipPrefix(zip.ZipOffset)
                : ArchiveOpenOptions.VerifiedZipStructure(zip);
        var rar = await RarVolumeHeader.FindJpegPrefixedRar5Async(path, length, ct);
        return rar.HasValue ? ArchiveOpenOptions.VerifiedRar5Prefix(rar.Value) : null;
    }
}
