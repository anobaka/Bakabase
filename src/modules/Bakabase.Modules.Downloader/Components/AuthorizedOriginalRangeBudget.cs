using System.Buffers;
using Bakabase.Modules.Downloader.Extensions;

namespace Bakabase.Modules.Downloader.Components;

/// <summary>Only providers opting into parallel transfers share these slots. Reserve the first
/// lane for probes and the lifetime of the transfer. Extra slots never wait while holding a lane.</summary>
internal sealed class AuthorizedOriginalRangeBudget
{
    internal const int MaximumLanes = 8;
    private readonly SemaphoreSlim _shared = new(MaximumLanes);
    private readonly ArrayPool<byte> _buffers = ArrayPool<byte>.Create(AuthorizedOriginalCheckpointStore.SegmentBytes, MaximumLanes);
    private readonly Dictionary<string, Client> _clients;

    internal AuthorizedOriginalRangeBudget(IEnumerable<DownloaderServiceCollectionExtensions.AuthorizedClient> clients) =>
        _clients = clients.GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new Client(this, group.Last()), StringComparer.Ordinal);

    internal Client RequireClient(string name) => _clients.TryGetValue(name, out var value) ? value :
        throw new Models.AuthorizedOriginalDownloadException("ORIGINAL_HTTP_LOCATION_UNAVAILABLE");

    internal sealed class Client(AuthorizedOriginalRangeBudget budget, DownloaderServiceCollectionExtensions.AuthorizedClient options)
    {
        private readonly SemaphoreSlim _slots = new(options.MaxParallelRanges);
        internal int WindowSize(long length) => length >= options.MinimumParallelBytes ? options.MaxParallelRanges : 1;

        internal async Task<IDisposable?> EnterAsync(CancellationToken ct)
        {
            if (options.MaxParallelRanges == 1) return null;
            await _slots.WaitAsync(ct);
            try { await budget._shared.WaitAsync(ct); }
            catch { _slots.Release(); throw; }
            return new Slot(this);
        }

        internal int TakeExtra(int maximum)
        {
            var count = 0;
            while (count < maximum && _slots.Wait(0))
            {
                if (!budget._shared.Wait(0))
                {
                    _slots.Release();
                    break;
                }
                count++;
            }
            return count;
        }

        internal void Release(int count)
        {
            if (count == 0) return;
            budget._shared.Release(count);
            _slots.Release(count);
        }

        internal byte[] Rent() => budget._buffers.Rent(AuthorizedOriginalCheckpointStore.SegmentBytes);
        internal void Return(byte[] buffer) => budget._buffers.Return(buffer);

        private sealed class Slot(Client client) : IDisposable
        {
            public void Dispose() => client.Release(1);
        }
    }
}
