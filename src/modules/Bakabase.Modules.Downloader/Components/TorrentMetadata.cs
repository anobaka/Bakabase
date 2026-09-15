using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MonoTorrent;

namespace Bakabase.Modules.Downloader.Components;

/// <summary>Format and path validation shared by uploads, URL downloads and magnet resolution.</summary>
public static class TorrentMetadata
{
    public const int MaxMetadataBytes = 4 * 1024 * 1024;

    public static void Validate(byte[] metadata) => Parse(metadata);

    public static bool IsValidMagnet(string? magnet) => MagnetLink.TryParse(magnet ?? "", out _);

    internal static Torrent Parse(byte[] metadata)
    {
        if (metadata.Length is 0 or > MaxMetadataBytes || !Torrent.TryLoad(metadata, out var torrent))
            throw new ArgumentException("Choose a valid torrent file no larger than 4 MiB.", nameof(metadata));
        ValidatePaths(torrent);
        return torrent;
    }

    internal static void ValidatePaths(Torrent torrent)
    {
        foreach (var file in torrent.Files)
        {
            var segments = file.Path.Split(['/', '\\']);
            if (Path.IsPathRooted(file.Path) || file.Path.StartsWith('/') || file.Path.StartsWith('\\') ||
                segments.Any(s => s is "" or "." or "..") || segments[0].Contains(':'))
                throw new ArgumentException("The torrent contains an absolute or unsafe relative file path.");
        }
    }

    public static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken ct = default)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > MaxMetadataBytes)
                throw new ArgumentException("Torrent metadata must be no larger than 4 MiB.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return output.ToArray();
    }
}
