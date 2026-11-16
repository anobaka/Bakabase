using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Artifacts;

/// <summary>Full-content SHA256 with bounded sequential reads, including network-mounted artifacts.</summary>
internal static class ArtifactContentHash
{
    internal const int BufferSize = 4 * 1024 * 1024;

    internal static async ValueTask<byte[]> ComputeAsync(Stream source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
            }
            ct.ThrowIfCancellationRequested();
            return hash.GetHashAndReset();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }
}
