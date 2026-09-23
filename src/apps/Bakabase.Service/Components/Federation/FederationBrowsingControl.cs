using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Modules.Federation.Security;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Service.Components.Federation;

/// <summary>Local browsing is opt-in, independent of grants and inbound library sharing.</summary>
public sealed class FederationBrowsingControl(FederationStateStore store, FederatedQueryCoordinator queries,
    FederationMediaSessions media) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource _lifetime = new();
    public Task<bool> IsEnabledAsync(CancellationToken ct = default) => store.IsBrowsingEnabledAsync(ct);

    public async Task<CancellationToken> GetLifetimeAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!await store.IsBrowsingEnabledAsync(ct))
                throw new FederationAccessException("BrowsingDisabled", 403, "Enable multi-device browsing on this device first.");
            return _lifetime.Token;
        }
        finally { _gate.Release(); }
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await store.SetBrowsingEnabledAsync(enabled, ct);
            if (enabled)
            {
                if (_lifetime.IsCancellationRequested) { _lifetime.Dispose(); _lifetime = new(); }
            }
            else
            {
                _lifetime.Cancel();
                media.Clear();
                await queries.ReleaseOwnerAsync("local-ui");
            }
        }
        finally { _gate.Release(); }
    }

    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); _gate.Dispose(); }
}

public sealed class FederationBrowsingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, FederationBrowsingControl browsing)
    {
        var path = context.Request.Path;
        var controlled = path.StartsWithSegments("/federation/local/resources") ||
            path.StartsWithSegments("/federation/local/media") ||
            path.StartsWithSegments("/federation/local/playback-sessions") ||
            path.StartsWithSegments("/federation/local/queries") && !HttpMethods.IsDelete(context.Request.Method);
        if (!controlled) { await next(context); return; }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,
            await browsing.GetLifetimeAsync(context.RequestAborted));
        context.RequestAborted = linked.Token;
        await next(context);
    }
}
