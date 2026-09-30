using System;
using System.IO;
using System.Linq;
using System.Text;
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
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.Length == 0)
            throw new ArgumentException("The torrent metadata is empty.", nameof(metadata));
        if (metadata.Length > MaxMetadataBytes)
            throw new ArgumentException("Torrent metadata must be no larger than 4 MiB.", nameof(metadata));
        if (Encoding.UTF8.GetString(metadata.AsSpan(0, Math.Min(metadata.Length, 256)))
                .TrimStart('\uFEFF', ' ', '\t', '\r', '\n').StartsWith('<'))
            throw new ArgumentException("The download returned an HTML or XML page instead of a torrent file. " +
                                        "Check that the torrent link is available and your login is valid.", nameof(metadata));

        Torrent torrent;
        try
        {
            // TryLoad suppresses the parsing exception, hiding the distinction between a bad
            // response and metadata unsupported by the torrent engine.
            torrent = Torrent.Load(metadata);
        }
        catch (Exception e)
        {
            throw new ArgumentException($"The downloaded data is not valid BitTorrent metadata ({metadata.Length} bytes).",
                nameof(metadata), e);
        }
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
