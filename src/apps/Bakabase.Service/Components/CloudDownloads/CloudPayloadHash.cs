using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Models;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.CloudDownloads;

/// <summary>Bounded sequential verification of large local or network-mounted payloads.</summary>
internal static class CloudPayloadHash
{
    internal const int BufferSize = 1024 * 1024;

    internal static async Task<VerifiedOriginalPayload> ComputeVerifiedFileAsync(string path, long expectedLength,
        CancellationToken ct, Func<ValueTask>? onRead = null)
    {
        ct.ThrowIfCancellationRequested();
        ArtifactManifestFiles.RejectLinks(path);
        using var file = RegularFileHandle.TryOpenRead(path) ?? throw new FileNotFoundException();
        var version = RegularFileHandle.ReadVersion(file, path);
        if (RandomAccess.GetLength(file.SafeFileHandle) != expectedLength) throw new IOException("The payload length changed.");
        var hash = await ComputeAsync(file, ct, onRead);
        ArtifactManifestFiles.RejectLinks(path);
        if (file.Position != expectedLength || RegularFileHandle.ReadVersion(file, path) != version)
            throw new IOException("The verified payload changed.");
        return new(path, expectedLength, hash.ToLowerInvariant());
    }

    internal static async Task<(long Length, string Sha256)> ComputeFileAsync(string path, CancellationToken ct, Func<ValueTask>? onRead = null)
    {
        ct.ThrowIfCancellationRequested();
        await using var file = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            // The pooled read buffer below supplies full-size reads. Disable a second
            // managed buffer; SequentialScan lets the filesystem optimize readahead.
            BufferSize = 1,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        var length = file.Length;
        return (length, await ComputeAsync(file, ct, onRead));
    }

    // The caller owns the stream. HashDataAsync uses small reads on some runtimes;
    // explicit large requests avoid that overhead on network-mounted payloads.
    internal static async Task<string> ComputeAsync(Stream source, CancellationToken ct, Func<ValueTask>? onRead = null)
    {
        ct.ThrowIfCancellationRequested();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var observed = source is FileStream ? FileIoObservationScope.Begin(read: true, write: false) : null;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false);
                if (read == 0) break;
                observed?.Add(read, 0);
                hash.AppendData(buffer, 0, read);
                if (onRead != null) await onRead().ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
