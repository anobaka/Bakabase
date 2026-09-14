using System.Security.Cryptography;
using System.Text.Json;
using Downloader;

namespace Bakabase.Modules.Downloader.Components;

/// <summary>
/// Binds the library's chunk positions to a remote version and to bytes actually saved on disk.
/// The library still owns chunk layout, requests and retries.
/// </summary>
internal sealed record HttpDownloadCheckpoint(HttpRemoteIdentity Identity, Chunk[] Chunks, string[] PrefixHashes)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<DownloadPackage?> ReadAsync(string directory, HttpRemoteIdentity identity,
        CancellationToken ct)
    {
        var manifest = Path.Combine(directory, "checkpoint.json");
        var partial = Path.Combine(directory, "payload.download");
        if (!identity.CanResume || !File.Exists(manifest) || !File.Exists(partial)) return null;
        try
        {
            var saved = JsonSerializer.Deserialize<HttpDownloadCheckpoint>(await File.ReadAllTextAsync(manifest, ct), Json);
            if (saved == null || saved.Identity != identity || saved.Chunks is not {Length: >= 1 and <= 16} ||
                saved.PrefixHashes == null || saved.Chunks.Length != saved.PrefixHashes.Length ||
                saved.Chunks.Any(chunk => chunk == null) || saved.PrefixHashes.Any(hash => hash == null)) return null;
            await using var stream = File.OpenRead(partial);
            long next = 0;
            for (var i = 0; i < saved.Chunks.Length; i++)
            {
                var chunk = saved.Chunks[i];
                if (chunk.Start != next || chunk.End < chunk.Start || chunk.End >= identity.Length ||
                    chunk.Position < 0 || chunk.Position > chunk.Length || stream.Length < chunk.Start + chunk.Position)
                    return null;
                if (await HashPrefixAsync(stream, chunk, ct) != saved.PrefixHashes[i]) return null;
                next = chunk.End + 1;
            }
            if (next != identity.Length) return null;
            return new DownloadPackage
            {
                FileName = Path.Combine(directory, "payload"),
                DownloadingFileExtension = ".download",
                TotalFileSize = identity.Length!.Value,
                IsSupportDownloadInRange = true,
                Chunks = saved.Chunks
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException)
        {
            return null;
        }
    }

    public static async Task SaveAsync(string directory, HttpRemoteIdentity identity, DownloadPackage package)
    {
        // The caller awaits DownloadFileTaskAsync/CloseAsync before entering here. Never hash
        // positions while the library's buffered writer can still be ahead of the file.
        if (!identity.CanResume || package.Chunks is not {Length: > 0} chunks ||
            !chunks.Any(c => c.Position > 0) || !File.Exists(package.DownloadingFileName)) return;
        var hashes = new string[chunks.Length];
        await using (var stream = new FileStream(package.DownloadingFileName, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            stream.Flush(flushToDisk: true);
            for (var i = 0; i < chunks.Length; i++)
                hashes[i] = await HashPrefixAsync(stream, chunks[i], CancellationToken.None);
        }
        var checkpoint = new HttpDownloadCheckpoint(identity, chunks, hashes);
        var temporary = Path.Combine(directory, "checkpoint.json.tmp");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(checkpoint, Json);
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, Path.Combine(directory, "checkpoint.json"), true);
    }

    private static async Task<string> HashPrefixAsync(FileStream stream, Chunk chunk, CancellationToken ct)
    {
        stream.Position = chunk.Start;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        var remaining = chunk.Position;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
            if (read == 0) throw new IOException("The saved download prefix is incomplete.");
            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static void Clear(string directory)
    {
        foreach (var name in new[] {"checkpoint.json", "checkpoint.json.tmp", "payload.download", "payload"})
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
