using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

/// <summary>Also bounds direct workflow/provider calls which do not pass through the download-center scheduler.</summary>
internal sealed class BaiduDownloadAdmission
{
    private readonly object _sync = new();
    private int _active;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<IDisposable> AcquireAsync(Func<int> capacity, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            Task changed;
            lock (_sync)
            {
                if (_active < Math.Clamp(capacity(), 1, BaiduDownloadOptions.MaximumConcurrentTasks))
                { _active++; return new Lease(this); }
                changed = _changed.Task;
            }
            await changed.WaitAsync(ct);
        }
    }

    public void NotifyChanged()
    {
        lock (_sync)
        {
            var previous = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            previous.TrySetResult();
        }
    }

    private void Release()
    {
        lock (_sync) { _active--; NotifyChanged(); }
    }

    private sealed class Lease(BaiduDownloadAdmission owner) : IDisposable
    {
        private BaiduDownloadAdmission? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
