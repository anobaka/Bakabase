using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>In-memory proof produced only by bounded, step-authorized structural inspection.</summary>
internal sealed record ZipStructureDescriptor(long SourceLength, long ZipOffset, long CentralDirectoryOffset,
    int CentralDirectorySize, int EntryCount, long ArchiveEnd, bool Zip64, string TailSha256, string CentralSha256);

/// <summary>
/// A small metadata-only hint for an already authorized extraction candidate. It never
/// scans payload, decompresses members, trusts filenames, or accepts multiple disks.
/// Single-disk ZIP64 records are accepted only when all mirrored sizes and offsets agree.
/// The installed archive tool must still confirm the same ZIP offset under its time budget.
/// </summary>
internal static class BoundedZipStructureProbe
{
    // This is a tool start-position bound, not a payload read allowance. Only the tail,
    // central directory and matching local headers are inspected within MaximumReadBytes.
    internal const long MaximumPrefixBytes = 512 * 1024 * 1024;
    internal const int MaximumReadBytes = 384 * 1024;
    private const int TailBytes = 128 * 1024;
    private const int MaximumTrailingBytes = 32 * 1024;
    private const int MaximumCentralBytes = 64 * 1024;
    private const int MaximumEntries = 128;
    private const int MaximumLocalHeaderBytes = 4096;
    private const int MaximumEndCandidates = 4;

    internal static async Task<long?> FindPrefixAsync(string path, long expectedLength, CancellationToken ct)
    {
        var structure = await FindStructureAsync(path, expectedLength, ct);
        if (structure?.ZipOffset >= MaximumPrefixBytes)
            throw new ArchiveFormatProbeIncompleteException(
                "ZIP structure was confirmed, but its media prefix exceeds the archive tool's bounded opening limit. Keep the files for manual processing or a future compatible tool; downloading them again will not remove this limitation.",
                ArchiveFormatProbeFailureCodes.PrefixLimit);
        return structure?.ZipOffset;
    }

    internal static async Task<ZipStructureDescriptor?> FindStructureAsync(string path, long expectedLength,
        CancellationToken ct, bool requireImageHeader = false)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1, FileOptions.Asynchronous | FileOptions.RandomAccess);
        if (file.Length != expectedLength) throw new InvalidDataException("The archive input changed during discovery.");
        var readBytes = 0;
        async Task<byte[]?> Read(long offset, int count)
        {
            ct.ThrowIfCancellationRequested();
            if (offset < 0 || count < 0 || offset > expectedLength - count) return null;
            if (count > MaximumReadBytes - readBytes)
                throw new ArchiveFormatProbeIncompleteException("Archive structure discovery exceeded its read limit. Revise the extraction step.");
            readBytes += count;
            var bytes = new byte[count];
            file.Position = offset;
            await file.ReadExactlyAsync(bytes, ct);
            return bytes;
        }
        // An archive already starting at byte zero takes precedence over a ZIP embedded
        // in its payload or trailer. Leave validation to its ordinary archive handler.
        var header = (await Read(0, (int)Math.Min(512, expectedLength)))!;
        if (ArchiveSignature.HasKnownContainerHeader(header))
            return null;
        if (requireImageHeader && !header.AsSpan().StartsWith(new byte[] {0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a}) &&
            !header.AsSpan().StartsWith(new byte[] {0xff, 0xd8, 0xff}))
            return null;
        var tailStart = Math.Max(0, expectedLength - TailBytes);
        var tail = (await Read(tailStart, (int)(expectedLength - tailStart)))!;
        ZipStructureDescriptor? found = null;
        var candidates = 0;
        for (var e = 0; e <= tail.Length - 22; e++)
        {
            ct.ThrowIfCancellationRequested();
            if (U32(tail, e) != 0x06054b50) continue;
            if (++candidates > MaximumEndCandidates)
                throw new ArchiveFormatProbeIncompleteException("The selected file has ambiguous archive end records. Revise the extraction step.");
            var end = tailStart + e;
            var commentLength = U16(tail, e + 20);
            var trailing = expectedLength - (end + 22 + commentLength);
            ulong count = U16(tail, e + 10);
            ulong centralSize = U32(tail, e + 12);
            ulong centralOffset = U32(tail, e + 16);
            var centralEnd = end;
            var zip64 = false;
            if (trailing < 0 || trailing > MaximumTrailingBytes ||
                U16(tail, e + 4) != 0 || U16(tail, e + 6) != 0) continue;
            if (e >= 20 && U32(tail, e - 20) == 0x07064b50)
            {
                // PKWARE APPNOTE 4.3.14/15: one fixed-size ZIP64 end record immediately
                // before the locator. Extensible records and split disks stay unsupported.
                // Derive the prefix independently from the directory and locator, rather
                // than seeking to an untrusted relative ZIP64 offset or scanning backwards.
                var z = e - 76;
                if (z < 0 || U32(tail, z) != 0x06064b50 || U64(tail, z + 4) != 44 ||
                    U32(tail, e - 16) != 0 || U32(tail, e - 4) != 1 ||
                    U32(tail, z + 16) != 0 || U32(tail, z + 20) != 0) continue;
                var entries = U64(tail, z + 32);
                var size = U64(tail, z + 40);
                var offset = U64(tail, z + 48);
                if (U64(tail, z + 24) != entries ||
                    !Mirror(U16(tail, e + 8), entries, ushort.MaxValue) ||
                    !Mirror(count, entries, ushort.MaxValue) ||
                    !Mirror(centralSize, size, uint.MaxValue) ||
                    !Mirror(centralOffset, offset, uint.MaxValue)) continue;
                count = entries; centralSize = size; centralOffset = offset;
                zip64 = true;
                centralEnd = tailStart + z;
                if (size > (ulong)centralEnd || offset > (ulong)centralEnd - size ||
                    U64(tail, e - 12) != size + offset) continue;
            }
            else if (count == ushort.MaxValue || centralSize == uint.MaxValue ||
                     centralOffset == uint.MaxValue || U16(tail, e + 8) != count) continue;
            if (count == 0 || count > MaximumEntries || centralSize == 0 ||
                centralSize > MaximumCentralBytes || centralSize > (ulong)centralEnd ||
                centralOffset >= (ulong)centralEnd - centralSize) continue;
            var centralStart = centralEnd - (long)centralSize;
            var prefix = centralStart - (long)centralOffset;
            if (prefix <= 0 || centralStart <= prefix) continue;
            var central = await Read(centralStart, (int)centralSize);
            if (central == null) continue;
            var position = 0;
            var valid = true;
            var intervals = new List<(long Start, long End)>();
            for (var entry = 0; entry < (int)count && valid; entry++)
            {
                if (position > central.Length - 46 || U32(central, position) != 0x02014b50) { valid = false; break; }
                var flags = U16(central, position + 8);
                var method = U16(central, position + 10);
                var crc = U32(central, position + 16);
                ulong packed = U32(central, position + 20);
                ulong unpacked = U32(central, position + 24);
                var nameLength = U16(central, position + 28);
                var extraLength = U16(central, position + 30);
                var noteLength = U16(central, position + 32);
                ulong localOffset = U32(central, position + 42);
                uint disk = U16(central, position + 34);
                var next = (long)position + 46 + nameLength + extraLength + noteLength;
                if (nameLength == 0 || nameLength > 1024 || next > central.Length ||
                    !ReadExtra(central.AsSpan(position + 46 + nameLength, extraLength),
                        ref unpacked, ref packed, ref localOffset, ref disk) || disk != 0 || unpacked > long.MaxValue ||
                    localOffset > (ulong)(centralStart - prefix) ||
                    centralStart - prefix - (long)localOffset < 30) { valid = false; break; }
                var localStart = prefix + (long)localOffset;
                var local = await Read(localStart, 30);
                if (local == null || U32(local, 0) != 0x04034b50 ||
                    U16(local, 6) != flags || U16(local, 8) != method || U16(local, 26) != nameLength ||
                    (flags & 8) == 0 && U32(local, 14) != crc)
                { valid = false; break; }
                var localExtra = U16(local, 28);
                var localLength = 30 + nameLength + localExtra;
                if (localLength > MaximumLocalHeaderBytes || localLength > centralStart - localStart ||
                    packed > (ulong)(centralStart - localStart - localLength)) { valid = false; break; }
                var dataEnd = localStart + localLength + (long)packed;
                var nameAndExtra = await Read(localStart + 30, nameLength + localExtra);
                ulong localUnpacked = U32(local, 22), localPacked = U32(local, 18), unusedOffset = 0;
                uint unusedDisk = 0;
                if (nameAndExtra == null ||
                    !nameAndExtra.AsSpan(0, nameLength).SequenceEqual(central.AsSpan(position + 46, nameLength)) ||
                    !ReadExtra(nameAndExtra.AsSpan(nameLength), ref localUnpacked, ref localPacked,
                        ref unusedOffset, ref unusedDisk) ||
                    (flags & 8) == 0 && (localPacked != packed || localUnpacked != unpacked))
                { valid = false; break; }
                intervals.Add((localStart, dataEnd));
                position = (int)next;
            }
            if (!valid || position != central.Length) continue;
            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
            if (intervals[0].Start != prefix) continue;
            for (var i = 1; i < intervals.Count; i++)
                if (intervals[i].Start < intervals[i - 1].End) valid = false;
            if (!valid) continue;
            if (found != null)
                throw new ArchiveFormatProbeIncompleteException("The selected file contains multiple plausible ZIP containers. Revise the extraction step.");
            found = new(expectedLength, prefix, centralStart, (int)centralSize, (int)count,
                end + 22 + commentLength, zip64,
                Convert.ToHexString(SHA256.HashData(tail)).ToLowerInvariant(),
                Convert.ToHexString(SHA256.HashData(central)).ToLowerInvariant());
        }
        if (file.Length != expectedLength) throw new InvalidDataException("The archive input changed during discovery.");
        // The structure can be known even when the installed CLI cannot seek directly to
        // it. -tzip:sN is a scan limit, not an offset; do not turn a larger proven prefix into
        // a larger blind scan or silently materialize another copy of a multi-GB archive.
        return found;
    }

    private static bool Mirror(ulong legacy, ulong value, ulong sentinel) => legacy == sentinel || legacy == value;

    private static bool ReadExtra(ReadOnlySpan<byte> extra, ref ulong unpacked, ref ulong packed,
        ref ulong offset, ref uint disk)
    {
        var needUnpacked = unpacked == uint.MaxValue;
        var needPacked = packed == uint.MaxValue;
        var needOffset = offset == uint.MaxValue;
        var needDisk = disk == ushort.MaxValue;
        var required = (needUnpacked ? 8 : 0) + (needPacked ? 8 : 0) + (needOffset ? 8 : 0) + (needDisk ? 4 : 0);
        var seen = false;
        for (var p = 0; p < extra.Length;)
        {
            if (extra.Length - p < 4) return false;
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(extra[p..]);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(extra[(p + 2)..]);
            if (length > extra.Length - p - 4) return false;
            if (kind == 1)
            {
                // Some writers retain an empty ZIP64 block in a central record whose
                // 32-bit values still fit. APPNOTE 4.5.3 permits values only for sentinel
                // fields: accept the empty block, never unsolicited or duplicate values.
                if (seen || length != required) return false;
                seen = true;
                var values = extra.Slice(p + 4, length);
                var at = 0;
                if (needUnpacked) { unpacked = BinaryPrimitives.ReadUInt64LittleEndian(values[at..]); at += 8; }
                if (needPacked) { packed = BinaryPrimitives.ReadUInt64LittleEndian(values[at..]); at += 8; }
                if (needOffset) { offset = BinaryPrimitives.ReadUInt64LittleEndian(values[at..]); at += 8; }
                if (needDisk) disk = BinaryPrimitives.ReadUInt32LittleEndian(values[at..]);
            }
            p += 4 + length;
        }
        return required == 0 || seen;
    }
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static ulong U64(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset));
}
