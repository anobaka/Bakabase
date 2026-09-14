using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using MonoTorrent;

namespace Bakabase.Service.Components.Acquisition.Downloads;

public interface IAcquisitionTorrentMetadataStore
{
    Task<string> SaveAsync(byte[] metadata, CancellationToken ct = default);
    Task<byte[]> ReadAsync(string reference, CancellationToken ct = default);
}

/// <summary>Uploaded metadata is addressed by hash, never by an arbitrary server path.</summary>
public sealed class AcquisitionTorrentMetadataStore : IAcquisitionTorrentMetadataStore
{
    public const int MaxMetadataBytes = 4 * 1024 * 1024;
    public const string ReferencePrefix = "bakabase-torrent:";
    private readonly Func<string> _appData;

    public AcquisitionTorrentMetadataStore(AppService appService) : this(() => appService.AppDataDirectory) { }
    internal AcquisitionTorrentMetadataStore(Func<string> appData) => _appData = appData;

    private string Root => Path.Combine(_appData(), "acquisition", "torrent-metadata");

    public async Task<string> SaveAsync(byte[] metadata, CancellationToken ct = default)
    {
        if (metadata.Length is 0 or > MaxMetadataBytes || !Torrent.TryLoad(metadata, out var torrent))
            throw new ArgumentException("Choose a valid torrent file no larger than 4 MiB.");
        BuiltInTorrentDownloader.ValidatePaths(torrent);
        var hash = Convert.ToHexString(SHA256.HashData(metadata)).ToLowerInvariant();
        Directory.CreateDirectory(Root);
        var target = Path.Combine(Root, hash + ".torrent");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, metadata, ct);
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return ReferencePrefix + hash;
    }

    public async Task<byte[]> ReadAsync(string reference, CancellationToken ct = default)
    {
        if (!IsManagedReference(reference)) throw new ArgumentException("Invalid uploaded torrent reference.");
        var hash = reference[ReferencePrefix.Length..].ToLowerInvariant();
        await using var stream = File.OpenRead(Path.Combine(Root, hash + ".torrent"));
        var bytes = await ReadBoundedAsync(stream, ct);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The uploaded torrent metadata no longer matches its reference.");
        return bytes;
    }

    public static bool IsManagedReference(string? value)
    {
        if (value == null || !value.StartsWith(ReferencePrefix, StringComparison.Ordinal) ||
            value.Length != ReferencePrefix.Length + 64) return false;
        foreach (var c in value.AsSpan(ReferencePrefix.Length))
            if (!Uri.IsHexDigit(c)) return false;
        return true;
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
