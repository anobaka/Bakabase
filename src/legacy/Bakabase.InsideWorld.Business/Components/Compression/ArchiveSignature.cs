using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>A cheap offset-zero signature, not proof that an archive can be opened.</summary>
public static class ArchiveSignature
{
    // Official format sources, reviewed 2026-10-20:
    // https://github.com/ip7z/7zip/blob/main/DOC/7zFormat.txt (SignatureHeader)
    // https://github.com/ip7z/7zip/blob/main/CPP/7zip/Archive/Zip/ZipHeader.h (local/EOCD signatures)
    // https://github.com/ip7z/7zip/blob/main/CPP/7zip/Archive/Rar/RarHandler.cpp (kMarker)
    // https://github.com/ip7z/7zip/blob/main/CPP/7zip/Archive/Rar/Rar5Handler.cpp (kMarker)
    // Deeper recognition belongs to the installed tool for explicitly selected inputs,
    // under its candidate budget. The filename never authorizes a signature.
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c })) return ".7z";
        if (header.StartsWith(new byte[] { 0x50, 0x4b, 0x03, 0x04 }) ||
            header.StartsWith(new byte[] { 0x50, 0x4b, 0x05, 0x06 })) return ".zip";
        if (header.StartsWith(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00 }) ||
            header.StartsWith(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0x00 })) return ".rar";
        return null;
    }

    /// <summary>Verify that a PNG/JPEG signature is followed by a structurally complete ZIP
    /// occupying most of the file. Only bounded metadata is read, never archive payloads.</summary>
    public static async Task<long?> FindDominantZipBehindImageAsync(string path, long length, CancellationToken ct)
    {
        var structure = await BoundedZipStructureProbe.FindStructureAsync(path, length, ct, requireImageHeader: true);
        return structure is {ZipOffset: > 0} && structure.ArchiveEnd - structure.ZipOffset > length / 2
            ? structure.ZipOffset : null;
    }

    // Used only to preserve an existing outer container before a deeper ZIP hint.
    // This does not expand the set of files admitted by ordinary workflow discovery.
    internal static bool HasKnownContainerHeader(ReadOnlySpan<byte> header) => Detect(header) != null ||
        header.StartsWith(new byte[] {0x50, 0x4b, 0x07, 0x08}) ||
        header.StartsWith(new byte[] {0x1f, 0x8b, 0x08}) ||
        header.StartsWith(new byte[] {0xfd, 0x37, 0x7a, 0x58, 0x5a, 0x00}) ||
        header.Length >= 4 && header.StartsWith("BZh"u8) && header[3] is >= (byte)'1' and <= (byte)'9' ||
        header.Length >= 262 && header.Slice(257, 5).SequenceEqual("ustar"u8);
}
