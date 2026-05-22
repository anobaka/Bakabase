using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;

namespace Bakabase.TestKit.Implementations;

/// <summary>
/// Test double for <see cref="IWebViewSession"/>. Records every operation invoked on it
/// (in-order) so tests can assert ordering and frequency, lets tests synthesize Navigated
/// events with <see cref="RaiseNavigatedAsync"/>, and exposes
/// <see cref="ConfirmAsync"/> / <see cref="CancelAsync"/> hooks to drive the orchestrator's
/// completion flow.
///
/// Navigated handlers run serialized (a SemaphoreSlim mirrors the real Avalonia session)
/// and exceptions are caught and recorded, so tests can verify both ordering invariants and
/// exception isolation without spinning up a real GUI.
/// </summary>
public sealed class FakeWebViewSession : IWebViewSession
{
    private readonly TaskCompletionSource _userActionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Func<string, Task>> _navigatedHandlers = new();
    private readonly object _handlersLock = new();
    private readonly SemaphoreSlim _handlerGate = new(1, 1);
    private bool _disposed;

    /// <summary>Cookies the session pretends are present, keyed by URL → list of (name, value).</summary>
    public Dictionary<string, List<(string Name, string Value)>> CookieStore { get; } = new();

    /// <summary>Append-only log of operations. Tests assert on this.</summary>
    public List<string> Operations { get; } = new();

    /// <summary>Exceptions caught while invoking Navigated handlers. Tests use to verify isolation.</summary>
    public ConcurrentBag<Exception> SwallowedHandlerExceptions { get; } = new();

    public string? CurrentUrl { get; private set; }

    public void OnNavigated(Func<string, Task> handler)
    {
        ThrowIfDisposed();
        lock (_handlersLock) _navigatedHandlers.Add(handler);
    }

    public Task NavigateAsync(string url)
    {
        ThrowIfDisposed();
        Operations.Add($"Navigate:{url}");
        CurrentUrl = url;
        return Task.CompletedTask;
    }

    public Task DeleteCookieAsync(string url, string name)
    {
        ThrowIfDisposed();
        Operations.Add($"DeleteCookie:{url}|{name}");
        if (CookieStore.TryGetValue(url, out var list))
            list.RemoveAll(c => c.Name == name);
        return Task.CompletedTask;
    }

    public Task MirrorCookiesAsync(string sourceUrl, string targetDomain, string[] cookieNames)
    {
        ThrowIfDisposed();
        Operations.Add($"Mirror:{sourceUrl}->{targetDomain}|[{string.Join(",", cookieNames)}]");

        if (!CookieStore.TryGetValue(sourceUrl, out var src)) return Task.CompletedTask;

        // Mirror under a synthetic URL key so tests can introspect what landed where. Real
        // implementation writes to the cookie jar at targetDomain — we record the fact.
        var targetKey = "domain:" + targetDomain;
        if (!CookieStore.TryGetValue(targetKey, out var dst))
            CookieStore[targetKey] = dst = new List<(string, string)>();

        foreach (var name in cookieNames)
        {
            var match = src.FirstOrDefault(c => c.Name == name);
            if (match.Name != null)
                dst.Add(match);
        }
        return Task.CompletedTask;
    }

    public Task<string?> GetCookiesAsync(string[] urls)
    {
        ThrowIfDisposed();
        Operations.Add($"GetCookies:[{string.Join(",", urls)}]");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parts = new List<string>();
        foreach (var url in urls)
        {
            if (!CookieStore.TryGetValue(url, out var list)) continue;
            foreach (var (name, value) in list)
            {
                if (seen.Add(name)) parts.Add($"{name}={value}");
            }
        }
        return Task.FromResult<string?>(parts.Count == 0 ? null : string.Join("; ", parts));
    }

    public Task WaitForUserConfirmAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return cancellationToken.CanBeCanceled
            ? _userActionTcs.Task.WaitAsync(cancellationToken)
            : _userActionTcs.Task;
    }

    public void SetStatusText(string text)
    {
        if (_disposed) return;
        Operations.Add($"Status:{text}");
    }

    /// <summary>Synthesize a Navigated event. Awaits all registered handlers serially —
    /// matching production semantics — so tests can assert handler-side effects deterministically.</summary>
    public async Task RaiseNavigatedAsync(string url)
    {
        await _handlerGate.WaitAsync();
        try
        {
            CurrentUrl = url;
            Operations.Add($"Navigated:{url}");

            Func<string, Task>[] snapshot;
            lock (_handlersLock) snapshot = _navigatedHandlers.ToArray();

            foreach (var handler in snapshot)
            {
                try
                {
                    await handler(url);
                }
                catch (Exception ex)
                {
                    SwallowedHandlerExceptions.Add(ex);
                }
            }
        }
        finally
        {
            _handlerGate.Release();
        }
    }

    public void Confirm() => _userActionTcs.TrySetResult();
    public void Cancel() =>
        _userActionTcs.TrySetException(new OperationCanceledException("Test cancelled."));

    /// <summary>Seed a cookie at the given URL key. URLs and "domain:..." keys are both supported.</summary>
    public void SeedCookie(string urlOrDomainKey, string name, string value)
    {
        if (!CookieStore.TryGetValue(urlOrDomainKey, out var list))
            CookieStore[urlOrDomainKey] = list = new List<(string, string)>();
        list.Add((name, value));
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return default;
        _disposed = true;
        _userActionTcs.TrySetException(new OperationCanceledException("Disposed."));
        _handlerGate.Dispose();
        return default;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FakeWebViewSession));
    }
}
