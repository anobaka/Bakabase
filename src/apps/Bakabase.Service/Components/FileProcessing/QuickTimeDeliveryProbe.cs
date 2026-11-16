using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>
/// Bounded QuickTime/MP4 container recognition, not playback validation or archive discovery.
/// Reads atom headers and seeks over payloads; no media decoding, shell commands or directory scans.
/// https://developer.apple.com/documentation/quicktime-file-format/atoms
/// https://developer.apple.com/documentation/quicktime-file-format/movie_atom
/// </summary>
internal static class QuickTimeDeliveryProbe
{
    internal const int MaximumAtoms = 128;
    private sealed class Budget { public int Remaining = MaximumAtoms; }
    private sealed record Atom(uint Type, long Payload, long End);
    private const uint Ftyp = 0x66747970, Moov = 0x6d6f6f76, Mdat = 0x6d646174;
    private const uint Mvhd = 0x6d766864, Trak = 0x7472616b, QtBrand = 0x71742020;

    internal static Task<bool> IsMovieAsync(string path, CancellationToken ct) =>
        IsMovieAsync(path, false, null, ct);

    // MP4RA, https://mp4ra.org/registered-types/brands. Only use the stricter MP4 result as
    // positive evidence against replaying instructions for an already extracted outer archive.
    internal static Task<bool> IsMp4Async(string path, long expectedLength, CancellationToken ct) =>
        IsMovieAsync(path, true, expectedLength, ct);

    private static async Task<bool> IsMovieAsync(string path, bool mp4, long? expectedLength, CancellationToken ct)
    {
        var before = new FileInfo(path);
        if (!before.Exists || (before.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        var length = before.Length; var modified = before.LastWriteTimeUtc;
        if (length < 32 || expectedLength != null && length != expectedLength) return false;
        try
        {
            ArtifactManifestFiles.RejectLinks(path);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.RandomAccess);
            var budget = new Budget();
            var movie = false; var data = false; var fileType = false;
            while (stream.Position < length)
            {
                var atom = await ReadAtomAsync(stream, length, budget, ct);
                if (atom == null || mp4 && !fileType && atom.Type != Ftyp) return false;
                switch (atom.Type)
                {
                    case Ftyp:
                        // QuickTime-compatible brands only. Other ISO containers retain the
                        // conservative review path rather than being accepted by their name.
                        var size = atom.End - atom.Payload;
                        if (fileType || size is < 8 or > 256 || size % 4 != 0) return false;
                        var brands = new byte[(int)size];
                        await stream.ReadExactlyAsync(brands, ct);
                        var compatible = AcceptsBrand(BinaryPrimitives.ReadUInt32BigEndian(brands), mp4);
                        for (var i = 8; i < brands.Length; i += 4)
                            compatible |= AcceptsBrand(BinaryPrimitives.ReadUInt32BigEndian(brands.AsSpan(i, 4)), mp4);
                        if (!compatible) return false;
                        fileType = true;
                        break;
                    case Moov:
                        if (movie || !await HasMovieStructureAsync(stream, atom.End, budget, mp4, ct)) return false;
                        movie = true;
                        break;
                    case Mdat:
                        data |= atom.End > atom.Payload;
                        break;
                    default:
                        // Unknown/extended boxes and nonempty padding can hold another format.
                        // Leave them unclassified. MOV delivery keeps its original acceptance rules.
                        if (mp4 && (atom.Type != 0x66726565 /* free */ || atom.End != atom.Payload))
                            return false;
                        break;
                }
                stream.Position = atom.End;
            }
            // Legacy QuickTime may omit ftyp; it still needs the movie header, track,
            // media data and a completely bounded top-level atom structure.
            if (mp4 && (!fileType || !movie || !data || await HasZipEndMarkerAsync(stream, length, ct)))
                return false;
            var after = new FileInfo(path);
            return movie && data && after.Exists && (after.Attributes & FileAttributes.ReparsePoint) == 0 &&
                   after.Length == length && after.LastWriteTimeUtc == modified;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool AcceptsBrand(uint brand, bool mp4) => mp4
        ? brand is 0x69736f6d or 0x6d703431 or 0x6d703432 or 0x61766331 // isom, mp41, mp42, avc1
        : brand == QtBrand;

    private static async Task<bool> HasZipEndMarkerAsync(FileStream stream, long length, CancellationToken ct)
    {
        // A valid movie can also carry a prefixed ZIP, including inside an mdat that covers
        // the file tail. A possible ZIP end marker is enough to abstain; don't scan payloads.
        var tail = new byte[(int)Math.Min(length, 65535 + 22 + 32768)];
        stream.Position = length - tail.Length;
        await stream.ReadExactlyAsync(tail, ct);
        return tail.AsSpan().IndexOf("PK\x05\x06"u8) >= 0;
    }

    private static async Task<bool> HasMovieStructureAsync(FileStream stream, long end, Budget budget, bool mp4, CancellationToken ct)
    {
        var header = false; var track = false;
        while (stream.Position < end)
        {
            var atom = await ReadAtomAsync(stream, end, budget, ct);
            if (atom == null) return false;
            if (atom.Type == Mvhd)
            {
                var version = new byte[1];
                if (atom.End - atom.Payload < 100) return false;
                await stream.ReadExactlyAsync(version, ct);
                if (version[0] > 1 || atom.End - atom.Payload < (version[0] == 1 ? 112 : 100)) return false;
                header = true;
            }
            else if (atom.Type == Trak)
                track |= mp4 ? await HasMediaTrackAsync(stream, atom.End, budget, ct) : atom.End - atom.Payload >= 8;
            stream.Position = atom.End;
        }
        return header && track;
    }

    private static async Task<bool> HasMediaTrackAsync(FileStream stream, long end, Budget budget, CancellationToken ct)
    {
        var trackHeader = false;
        var mediaHandler = false;
        while (stream.Position < end)
        {
            var child = await ReadAtomAsync(stream, end, budget, ct);
            if (child == null) return false;
            if (child.Type == 0x746b6864 /* tkhd */) trackHeader |= child.End - child.Payload >= 84;
            if (child.Type == 0x6d646961 /* mdia */)
            {
                while (stream.Position < child.End)
                {
                    var box = await ReadAtomAsync(stream, child.End, budget, ct);
                    if (box == null) return false;
                    if (box.Type == 0x68646c72 /* hdlr */ && box.End - box.Payload >= 24)
                    {
                        stream.Position += 8;
                        var handler = new byte[4];
                        await stream.ReadExactlyAsync(handler, ct);
                        mediaHandler |= handler.AsSpan().SequenceEqual("vide"u8) || handler.AsSpan().SequenceEqual("soun"u8);
                    }
                    stream.Position = box.End;
                }
            }
            stream.Position = child.End;
        }
        return trackHeader && mediaHandler;
    }

    private static async Task<Atom?> ReadAtomAsync(FileStream stream, long end, Budget budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var start = stream.Position;
        if (--budget.Remaining < 0 || end - start < 8) return null;
        var bytes = new byte[8]; await stream.ReadExactlyAsync(bytes, ct);
        ulong size = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        var type = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
        if (size == 1)
        {
            if (end - stream.Position < 8) return null;
            await stream.ReadExactlyAsync(bytes, ct);
            size = BinaryPrimitives.ReadUInt64BigEndian(bytes);
        }
        else if (size == 0) size = (ulong)(end - start);
        if (size < (ulong)(stream.Position - start) || size > (ulong)(end - start)) return null;
        return new(type, stream.Position, start + (long)size);
    }
}
