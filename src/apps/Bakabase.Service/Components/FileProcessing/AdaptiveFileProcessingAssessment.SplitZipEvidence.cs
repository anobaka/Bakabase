using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class AdaptiveFileProcessingAssessmentService
{
    private static readonly Regex MaskedZipSuffix = new(@"^\.z(?<mask>[^.0-9]{1,32})(?<end>[0-9]{2}|ip)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));

    // PKWARE APPNOTE 4.3 and 8.5. Restrict this hint to a single member spanning all
    // disks. One local header and a bounded tail can then establish the full extent.
    // Multi-member, partially masked and unsupported ZIP64 variants get no rename advice.
    private static async Task<AdaptiveMultipartNamingEvidence?> InspectMaskedSplitZipAsync(ArtifactRecord artifact,
        CancellationToken ct)
    {
        var entries = artifact.Manifest.Entries;
        var members = entries.Select((entry, index) =>
        {
            var stem = Path.GetFileNameWithoutExtension(entry.RelativePath);
            var match = MaskedZipSuffix.Match(Path.GetExtension(entry.RelativePath));
            var suffix = match.Groups["end"].Value;
            var disk = int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n - 1 : entries.Count - 1;
            return (entry, index, stem, match, suffix, disk);
        }).OrderBy(m => m.disk).ToArray();
        if (members.Any(m => !m.match.Success || m.stem.Length == 0) ||
            members.Select(m => m.stem).Distinct(StringComparer.Ordinal).Count() != 1 ||
            members.Select(m => m.match.Groups["mask"].Value).Distinct(StringComparer.Ordinal).Count() != 1 ||
            !members.Select(m => m.disk).SequenceEqual(Enumerable.Range(0, members.Length)) ||
            !members[^1].suffix.Equals("ip", StringComparison.OrdinalIgnoreCase) ||
            members.SkipLast(1).Any(m => m.entry.Length != members[0].entry.Length) ||
            members[^1].entry.Length > members[0].entry.Length) return null;
        try
        {
            var paths = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
            var files = members.Select(m => new FileInfo(paths[m.index])).ToArray();
            var timestamps = files.Select(f => f.LastWriteTimeUtc).ToArray();
            byte[] first = [], tail = [];
            for (var i = 0; i < files.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var file = files[i];
                ArtifactManifestFiles.RejectLinks(file.FullName);
                if (file.Length != members[i].entry.Length) return null;
                await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                    1, FileOptions.Asynchronous | FileOptions.RandomAccess);
                var header = new byte[(int)Math.Min(i == 0 ? 4096 : 32, file.Length)];
                await stream.ReadExactlyAsync(header, ct);
                if (i == 0) first = header;
                else if (ArchiveSignature.Detect(header) != null || U32(header, 0) == 0x08074b50) return null;
                if (i == files.Length - 1)
                {
                    tail = new byte[(int)Math.Min(file.Length, 65536)];
                    stream.Seek(-tail.Length, SeekOrigin.End);
                    await stream.ReadExactlyAsync(tail, ct);
                }
            }
            var total = members.Sum(m => m.entry.Length);
            if (!IsSingleMemberSplitZip(first, tail, files.Length, files[0].Length, files[^1].Length, total)) return null;
            for (var i = 0; i < files.Length; i++)
            {
                var file = files[i];
                ArtifactManifestFiles.RejectLinks(file.FullName);
                file.Refresh();
                if (file.Length != members[i].entry.Length || file.LastWriteTimeUtc != timestamps[i]) return null;
            }
            var renames = members.Select(m => new MultipartRename(m.index + 1, m.stem + ".z" + m.suffix.ToLowerInvariant())).ToArray();
            return new(renames.Select(r => r.InputId).ToArray(), renames, total, "zip");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException)
        { return null; }
    }

    private static bool IsSingleMemberSplitZip(byte[] first, byte[] tail, int disks, long firstSize, long finalSize, long total)
    {
        if (first.Length < 34 || U32(first, 0) != 0x08074b50 || U32(first, 4) != 0x04034b50) return false;
        var end = tail.Length - 22;
        if (end < 0 || U32(tail, end) != 0x06054b50 || U16(tail, end + 20) != 0 ||
            U16(tail, end + 4) != disks - 1 || U16(tail, end + 6) != disks - 1 ||
            U16(tail, end + 8) != 1 || U16(tail, end + 10) != 1) return false;
        var centralOffset = U32(tail, end + 16);
        var centralSize = U32(tail, end + 12);
        var start = centralOffset - (finalSize - tail.Length);
        if (start < 0 || start > end - 46 || centralSize < 46 || start + centralSize > end) return false;
        var c = (int)start;
        if (U32(tail, c) != 0x02014b50 || U16(tail, c + 34) != 0 || U32(tail, c + 42) != 4 ||
            U16(tail, c + 8) != U16(first, 10) || U16(tail, c + 10) != U16(first, 12)) return false;
        var name = U16(tail, c + 28);
        var extra = U16(tail, c + 30);
        if (46 + name + extra + U16(tail, c + 32) != centralSize || name == 0 ||
            name != U16(first, 30) || name > first.Length - 34 ||
            !tail.AsSpan(c + 46, name).SequenceEqual(first.AsSpan(34, name))) return false;
        var afterCentral = (int)(start + centralSize);
        if (afterCentral != end)
        {
            // For a compact ZIP64 end record, all its disk numbers, offsets and
            // lengths must mirror the classic record and the ZIP64 locator.
            var z = afterCentral;
            if (end - z != 76 || U32(tail, z) != 0x06064b50 || U64(tail, z + 4) != 44 ||
                U32(tail, z + 16) != disks - 1 || U32(tail, z + 20) != disks - 1 ||
                U64(tail, z + 24) != 1 || U64(tail, z + 32) != 1 ||
                U64(tail, z + 40) != centralSize || U64(tail, z + 48) != centralOffset ||
                U32(tail, z + 56) != 0x07064b50 || U32(tail, z + 60) != disks - 1 ||
                U64(tail, z + 64) != (ulong)centralOffset + centralSize || U32(tail, z + 72) != disks) return false;
        }
        ulong compressed = U32(tail, c + 20);
        if (compressed == uint.MaxValue)
        {
            var extras = tail.AsSpan(c + 46 + name, extra);
            var compressedOffset = U32(tail, c + 24) == uint.MaxValue ? 8 : 0;
            var found = false;
            while (extras.Length >= 4)
            {
                var size = BinaryPrimitives.ReadUInt16LittleEndian(extras[2..]);
                if (size > extras.Length - 4) return false;
                if (BinaryPrimitives.ReadUInt16LittleEndian(extras) == 1)
                {
                    if (found || size < compressedOffset + 8) return false;
                    compressed = BinaryPrimitives.ReadUInt64LittleEndian(extras[(4 + compressedOffset)..]);
                    found = true;
                }
                extras = extras[(4 + size)..];
            }
            if (!found || !extras.IsEmpty) return false;
        }
        // A streaming ZIP can leave local sizes/CRC unset. If populated, they must
        // agree with the catalog. The local header itself must fit on disk zero.
        var localCompressed = U32(first, 22);
        var localCrc = U32(first, 18);
        if (localCompressed is not (0 or uint.MaxValue) && localCompressed != compressed ||
            localCrc != 0 && localCrc != U32(tail, c + 16)) return false;
        var dataStart = 34 + name + U16(first, 32);
        var extent = total - finalSize + centralOffset - dataStart;
        return dataStart < firstSize && extent > 0 && compressed == (ulong)extent;
    }

    private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    private static ulong U64(byte[] data, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset));
}
