using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;

/// <summary>Business owners may fence starting a retained download while their task is being removed.</summary>
public interface IDownloadTaskStartGuard
{
    Task<IAsyncDisposable> AcquireAsync(int downloadTaskId, CancellationToken ct = default);
}

public static class DownloadTaskStartGuards
{
    public static async Task<IAsyncDisposable> AcquireAsync(IServiceProvider services, int downloadTaskId, CancellationToken ct = default)
    {
        var leases = new List<IAsyncDisposable>();
        try
        {
            foreach (var guard in services.GetServices<IDownloadTaskStartGuard>())
                leases.Add(await guard.AcquireAsync(downloadTaskId, ct));
            return new Lease(leases);
        }
        catch
        {
            await new Lease(leases).DisposeAsync(); throw;
        }
    }
    private sealed class Lease(List<IAsyncDisposable> leases) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        { for (var i = leases.Count - 1; i >= 0; i--) await leases[i].DisposeAsync(); }
    }
}
