using System.Collections.Concurrent;

namespace Bakabase.Modules.Federation.Security;

/// <summary>Long-lived reads link this token with the HTTP request's cancellation token.</summary>
public sealed class GrantLeaseRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _leases = new(StringComparer.Ordinal);

    public CancellationToken GetCancellationToken(string grantId) =>
        _leases.GetOrAdd(grantId, _ => new CancellationTokenSource()).Token;

    public static string OutboundKey(string grantId) => "outbound:" + grantId;

    public void Revoke(string grantId)
    {
        var source = _leases.GetOrAdd(grantId, _ => new CancellationTokenSource());
        try { source.Cancel(); }
        catch (AggregateException) { /* A consumer must not prevent other reads being cancelled. */ }
    }

    public void CancelAll()
    {
        foreach (var grantId in _leases.Keys) Revoke(grantId);
    }

    public void CancelInbound()
    {
        foreach (var grantId in _leases.Keys)
            if (!grantId.StartsWith("outbound:", StringComparison.Ordinal)) Revoke(grantId);
    }

    public void Resume()
    {
        foreach (var (id, source) in _leases)
            if (!id.StartsWith("outbound:", StringComparison.Ordinal) && source.IsCancellationRequested &&
                ((ICollection<KeyValuePair<string, CancellationTokenSource>>)_leases).Remove(new(id, source)))
                source.Dispose();
    }

    public void Resume(string grantId)
    {
        if (_leases.TryGetValue(grantId, out var source) && source.IsCancellationRequested &&
            ((ICollection<KeyValuePair<string, CancellationTokenSource>>)_leases).Remove(new(grantId, source))) source.Dispose();
    }

    public void Dispose()
    {
        CancelAll();
        foreach (var source in _leases.Values) source.Dispose();
    }
}
