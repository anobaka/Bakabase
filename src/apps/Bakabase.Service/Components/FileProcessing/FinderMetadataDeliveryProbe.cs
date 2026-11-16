using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>
/// Bounded Finder metadata recognition for delivery only. Does not inspect folder records,
/// discover unregistered files, or grant deletion/extraction authority.
/// Buddy header layout: https://github.com/dmgbuild/ds_store/blob/main/src/ds_store/buddy.py
/// </summary>
internal static class FinderMetadataDeliveryProbe
{
    internal const int MaximumBytes = 16 * 1024 * 1024;
    private const int HeaderBytes = 36;

    internal static bool IsCandidate(string path) =>
        string.Equals(Path.GetFileName(path), ".DS_Store", StringComparison.Ordinal);

    internal static async Task<bool> IsMetadataAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsCandidate(path)) return false;
        try
        {
            ArtifactManifestFiles.RejectLinks(path);
            var before = new FileInfo(path);
            if (!before.Exists || before.Length is < HeaderBytes or > MaximumBytes) return false;
            var length = before.Length;
            var modified = before.LastWriteTimeUtc;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                HeaderBytes, FileOptions.Asynchronous | FileOptions.RandomAccess);
            var header = new byte[HeaderBytes];
            await stream.ReadExactlyAsync(header, ct);
            if (!header.AsSpan(0, 8).SequenceEqual("\0\0\0\u0001Bud1"u8)) return false;
            var rootOffset = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4));
            var rootSize = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12, 4));
            var repeatedOffset = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16, 4));
            // Buddy offsets start after the four-byte version. Reject truncated or contradictory
            // headers instead of accepting an arbitrary user file solely by its reserved name.
            if (rootOffset != repeatedOffset || rootOffset < HeaderBytes - 4 || rootSize < 8 ||
                4L + rootOffset + rootSize > length) return false;
            before.Refresh();
            return before.Exists && (before.Attributes & FileAttributes.ReparsePoint) == 0 &&
                   before.Length == length && before.LastWriteTimeUtc == modified;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
}
