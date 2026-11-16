using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Compressors.Deflate;

namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>Bounded metadata-only protection against Unix 7-Zip's lossy UTF-8 decoding.
/// This identifies loss, never an original code page, and never reads member payload.</summary>
internal static class ZipNameEncodingProbe
{
    private const int TailBytes = ushort.MaxValue + 22;
    private const int MaximumCentralBytes = 16 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static async Task EnsureLosslessAsync(string path, long prefix, CancellationToken ct)
    {
        var before = new FileInfo(path);
        if (!before.Exists) return;
        var length = before.Length; var modified = before.LastWriteTimeUtc;
        if (before.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("ZIP name inspection requires a regular file.");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1, FileOptions.Asynchronous | FileOptions.RandomAccess);
        if (prefix < 0 || prefix > length - 4) return;
        var signature = new byte[4]; file.Position = prefix;
        await file.ReadExactlyAsync(signature, ct);
        if (U32(signature, 0) is not (0x04034b50 or 0x06054b50)) return;
        var tailStart = Math.Max(prefix, length - TailBytes);
        var tail = new byte[(int)(length - tailStart)]; file.Position = tailStart;
        await file.ReadExactlyAsync(tail, ct);
        var remainingCatalogBytes = MaximumCentralBytes;
        for (var end = tail.Length - 22; end >= 0; end--)
        {
            ct.ThrowIfCancellationRequested();
            if (U32(tail, end) != 0x06054b50 || end + 22 + U16(tail, end + 20) != tail.Length) continue;
            var count = U16(tail, end + 10); var size = U32(tail, end + 12); var offset = U32(tail, end + 16);
            // Split/ZIP64 containers retain their existing format handling. An explicit
            // decoder also remains authoritative; this guard does not reinterpret them.
            if (U16(tail, end + 4) != 0 || U16(tail, end + 6) != 0 || U16(tail, end + 8) != count ||
                count == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue ||
                prefix + offset + size != tailStart + end) continue;
            if (size > remainingCatalogBytes)
                throw new ArchiveFormatProbeIncompleteException("ZIP filename inspection exceeded its bounded catalog limit.");
            remainingCatalogBytes -= (int)size;
            var central = new byte[(int)size]; file.Position = prefix + offset;
            await file.ReadExactlyAsync(central, ct);
            var lossy = HasLossyNames(central, count);
            if (lossy == null) continue;
            var after = new FileInfo(path);
            if (!after.Exists || file.Length != length || after.Length != length || after.LastWriteTimeUtc != modified)
                throw new InvalidDataException("The ZIP input changed during filename inspection.");
            if (lossy == true)
                throw new ArchiveNameEncodingUnsupportedException(
                    "The ZIP contains filename bytes that the default archive tool cannot decode without loss. Confirm the ZIP filename encoding in this extraction step. No files have been extracted or renamed.");
            return;
        }
    }

    // null means the complete catalog was not established. A malformed record must not
    // be mistaken for evidence about name bytes or authorize a code page guess.
    private static bool? HasLossyNames(byte[] central, int count)
    {
        var position = 0; var lossy = false;
        for (var i = 0; i < count; i++)
        {
            if (position > central.Length - 46 || U32(central, position) != 0x02014b50) return null;
            var flags = U16(central, position + 8); var nameLength = U16(central, position + 28);
            var extraLength = U16(central, position + 30); var commentLength = U16(central, position + 32);
            var next = (long)position + 46 + nameLength + extraLength + commentLength;
            if (nameLength == 0 || next > central.Length || U16(central, position + 34) != 0) return null;
            var name = central.AsSpan(position + 46, nameLength);
            var extra = central.AsSpan(position + 46 + nameLength, extraLength);
            if (!IsUtf8(name) && ((flags & 0x800) != 0 || !HasUnicodeName(extra, name))) lossy = true;
            position = (int)next;
        }
        return position == central.Length ? lossy : null;
    }

    private static bool HasUnicodeName(ReadOnlySpan<byte> extra, ReadOnlySpan<byte> original)
    {
        for (var position = 0; position <= extra.Length - 4;)
        {
            var id = BinaryPrimitives.ReadUInt16LittleEndian(extra[position..]);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(extra[(position + 2)..]);
            if (length > extra.Length - position - 4) return false;
            if (id == 0x7075)
            {
                var field = extra.Slice(position + 4, length);
                if (field.Length < 6 || field[0] > 1 || field[5..].Contains((byte)0) || !IsUtf8(field[5..])) return false;
                var crc = new CRC32(); var bytes = original.ToArray(); crc.SlurpBlock(bytes, 0, bytes.Length);
                return (uint)crc.Crc32Result == BinaryPrimitives.ReadUInt32LittleEndian(field[1..]);
            }
            position += 4 + length;
        }
        return false;
    }

    private static bool IsUtf8(ReadOnlySpan<byte> bytes)
    {
        try { StrictUtf8.GetCharCount(bytes); return true; }
        catch (DecoderFallbackException) { return false; }
    }
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
