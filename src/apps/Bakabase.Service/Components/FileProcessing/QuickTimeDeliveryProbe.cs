using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>
/// Bounded container recognition for MOV delivery, not playback validation or archive discovery.
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

    internal static async Task<bool> IsMovieAsync(string path, CancellationToken ct)
    {
        var before = new FileInfo(path);
        if (!before.Exists || (before.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        var length = before.Length; var modified = before.LastWriteTimeUtc;
        if (length < 32) return false;
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
                if (atom == null) return false;
                switch (atom.Type)
                {
                    case Ftyp:
                        // QuickTime-compatible brands only. Other ISO containers retain the
                        // conservative review path rather than being accepted by their name.
                        var size = atom.End - atom.Payload;
                        if (fileType || size is < 8 or > 256 || size % 4 != 0) return false;
                        var brands = new byte[(int)size];
                        await stream.ReadExactlyAsync(brands, ct);
                        var compatible = BinaryPrimitives.ReadUInt32BigEndian(brands) == QtBrand;
                        for (var i = 8; i < brands.Length; i += 4)
                            compatible |= BinaryPrimitives.ReadUInt32BigEndian(brands.AsSpan(i, 4)) == QtBrand;
                        if (!compatible) return false;
                        fileType = true;
                        break;
                    case Moov:
                        if (movie || !await HasMovieStructureAsync(stream, atom.End, budget, ct)) return false;
                        movie = true;
                        break;
                    case Mdat:
                        data |= atom.End > atom.Payload;
                        break;
                }
                stream.Position = atom.End;
            }
            // Legacy QuickTime may omit ftyp; it still needs the movie header, track,
            // media data and a completely bounded top-level atom structure.
            var after = new FileInfo(path);
            return movie && data && after.Exists && (after.Attributes & FileAttributes.ReparsePoint) == 0 &&
                   after.Length == length && after.LastWriteTimeUtc == modified;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static async Task<bool> HasMovieStructureAsync(FileStream stream, long end, Budget budget, CancellationToken ct)
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
            else if (atom.Type == Trak) track |= atom.End - atom.Payload >= 8;
            stream.Position = atom.End;
        }
        return header && track;
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
