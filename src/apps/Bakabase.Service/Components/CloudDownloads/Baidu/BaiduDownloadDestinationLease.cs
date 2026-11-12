using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

/// <summary>Different operation IDs do not make overlapping payload/checkpoint paths safe to write concurrently.</summary>
internal sealed class BaiduDownloadDestinationLease
{
    private readonly object _sync = new();
    private readonly HashSet<Lease> _active = [];
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<IDisposable> AcquireAsync(string directory, CancellationToken ct)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (new DirectoryInfo(current).LinkTarget != null)
                throw new CloudProviderException("BAIDU_UNSAFE_REMOTE_PATH");
        }
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            Task changed;
            lock (_sync)
            {
                if (!_active.Any(active => Overlaps(active.Path, path)))
                {
                    var lease = new Lease(this, path);
                    _active.Add(lease);
                    return lease;
                }
                changed = _changed.Task;
            }
            await changed.WaitAsync(ct);
        }
    }

    private static bool Overlaps(string a, string b) => Within(a, b) || Within(b, a);
    private static bool Within(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private void Release(Lease lease)
    {
        lock (_sync)
        {
            _active.Remove(lease);
            var previous = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            previous.TrySetResult();
        }
    }

    private sealed class Lease(BaiduDownloadDestinationLease owner, string path) : IDisposable
    {
        public string Path { get; } = path;
        private BaiduDownloadDestinationLease? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(this);
    }
}
