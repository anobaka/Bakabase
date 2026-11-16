using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Compressors.Deflate;

namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>Bounded main-header evidence for ambiguous numeric filename suffixes.
/// This does not validate file contents or replace the extraction tool's checks.</summary>
public static class RarVolumeHeader
{
    // RAR 5 main-header archive flags and CRC: https://www.rarlab.com/technote.htm
    // RAR 4 main-header layout: https://github.com/ip7z/7zip/blob/main/CPP/7zip/Archive/Rar/RarHeader.h
    // Reviewed 2026-11-12. Encrypted, damaged, prefixed or unsupported headers remain unknown.
    internal const int MaximumJpegPrefixBytes = 1024 * 1024;
    private const int MaximumHeaderBytes = 64 * 1024;
    private static ReadOnlySpan<byte> Version5Marker => [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0x00];

    // A .rar suffix makes 7-Zip select its older RAR handler ahead of RAR5 during
    // embedded discovery. Inspect only selected .rar files beginning with JPEG.
    internal static async Task<long?> FindJpegPrefixedRar5Async(string path, long length, CancellationToken ct)
    {
        if (length < 24 || !Path.GetExtension(path).Equals(".rar", StringComparison.OrdinalIgnoreCase))
            return null;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var start = new byte[3];
        await file.ReadExactlyAsync(start, ct);
        if (start[0] != 0xff || start[1] != 0xd8 || start[2] != 0xff) return null;
        var prefix = new byte[(int)Math.Min(length - start.Length, MaximumJpegPrefixBytes + Version5Marker.Length - start.Length)];
        await file.ReadExactlyAsync(prefix, ct);
        var marker = prefix.AsSpan().IndexOf(Version5Marker);
        if (marker < 2 || prefix[marker - 2] != 0xff || prefix[marker - 1] != 0xd9) return null;
        var offset = marker + start.Length;
        file.Position = offset;
        var header = new byte[(int)Math.Min(length - offset, MaximumHeaderBytes)];
        await file.ReadExactlyAsync(header, ct);
        return ConfirmsSingleArchive(header, header.Length) ? offset : null;
    }

    public static bool ConfirmsSingleArchive(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[MaximumHeaderBytes];
        var length = input.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return ConfirmsSingleArchive(buffer, length);
    }

    private static bool ConfirmsSingleArchive(byte[] buffer, int length)
    {
        var data = buffer.AsSpan(0, length);
        if (data.StartsWith(Version5Marker))
        {
            const int crcOffset = 8;
            var cursor = crcOffset + 4;
            if (data.Length <= cursor || !ReadNumber(data, ref cursor, out var size) ||
                size > (ulong)(data.Length - cursor)) return false;
            var end = cursor + (int)size;
            var crc = Checksum(buffer, crcOffset + 4, end - crcOffset - 4);
            if (crc != BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(crcOffset, 4))) return false;
            var header = data[..end];
            if (!ReadNumber(header, ref cursor, out var type) || type != 1 ||
                !ReadNumber(header, ref cursor, out var flags) || (flags & (2UL | 8UL | 16UL)) != 0)
                return false;
            ulong extra = 0;
            if ((flags & 1) != 0 && !ReadNumber(header, ref cursor, out extra)) return false;
            if (!ReadNumber(header, ref cursor, out var archiveFlags) ||
                extra > (ulong)(end - cursor)) return false;
            // A volume number is also positive volume evidence, even in an inconsistent header.
            return (archiveFlags & 3) == 0;
        }
        if (data.StartsWith(new byte[] {0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00}))
        {
            const int start = 7;
            if (data.Length < start + 13 || data[start + 2] != 0x73) return false;
            var size = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(start + 5, 2));
            if (size < 13 || size > data.Length - start) return false;
            if ((ushort)Checksum(buffer, start + 2, size - 2) !=
                BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(start, 2))) return false;
            return (BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(start + 3, 2)) & 1) == 0;
        }
        return false;
    }

    private static uint Checksum(byte[] bytes, int start, int length)
    {
        var crc = new CRC32();
        crc.SlurpBlock(bytes, start, length);
        return (uint)crc.Crc32Result;
    }

    private static bool ReadNumber(ReadOnlySpan<byte> bytes, ref int cursor, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift < 64 && cursor < bytes.Length; shift += 7)
        {
            var b = bytes[cursor++];
            if (shift == 63 && b > 1) return false;
            value |= (ulong)(b & 0x7f) << shift;
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }
}
