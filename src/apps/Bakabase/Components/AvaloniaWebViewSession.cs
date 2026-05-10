using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Bakabase.Controls;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Windows;

namespace Bakabase.Components;

/// <summary>
/// Avalonia-backed implementation of <see cref="IWebViewSession"/>. Wraps a single
/// <see cref="CookieCaptureWindow"/> + its embedded <see cref="NativeWebViewHost"/>,
/// translates the window's UI events (Confirm / Cancel / Closing) into a
/// <see cref="WaitForUserConfirmAsync"/>-shaped lifecycle, and serializes async Navigated
/// handlers so chain-style logic doesn't race.
/// </summary>
internal sealed class AvaloniaWebViewSession : IWebViewSession
{
    private readonly CookieCaptureWindow _window;
    private readonly NativeWebViewHost _webView;
    private readonly TaskCompletionSource _userActionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly List<Func<string, Task>> _navigatedHandlers = new();
    private readonly object _handlersLock = new();
    private readonly SemaphoreSlim _handlerGate = new(1, 1);

    private bool _disposed;

    public AvaloniaWebViewSession(CookieCaptureWindow window)
    {
        _window = window;
        _webView = window.WebView;

        _window.ConfirmClicked += () => _userActionTcs.TrySetResult();
        _window.CancelClicked += () =>
            _userActionTcs.TrySetException(new OperationCanceledException("User cancelled."));
        _window.Closing += (_, _) =>
            _userActionTcs.TrySetException(new OperationCanceledException("Window closed."));

        _webView.Navigated += OnWebViewNavigated;
    }

    public string? CurrentUrl => _webView.GetCurrentUrl();

    public void OnNavigated(Func<string, Task> handler)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(handler);
        lock (_handlersLock) _navigatedHandlers.Add(handler);
    }

    private async void OnWebViewNavigated(string url)
    {
        // async-void event handler — top-level catch is required so a thrown handler
        // can't escape into Avalonia's dispatcher and crash the app.
        try
        {
            // Serialize handler invocations: chain logic relies on each Navigated being
            // fully processed (mirror cookies, navigate to next URL) before the next one
            // fires. SourceChanged events that arrive while the previous handler is still
            // running queue up here.
            await _handlerGate.WaitAsync().ConfigureAwait(false);
            try
            {
                Func<string, Task>[] snapshot;
                lock (_handlersLock) snapshot = _navigatedHandlers.ToArray();

                foreach (var handler in snapshot)
                {
                    try
                    {
                        await handler(url).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AvaloniaWebViewSession] Navigated handler threw: {ex}");
                    }
                }
            }
            finally
            {
                _handlerGate.Release();
            }
        }
        catch (Exception ex)
        {
            // Disposed during await, gate disposed, etc. Log and swallow — the session is
            // tearing down anyway.
            Console.WriteLine($"[AvaloniaWebViewSession] OnWebViewNavigated outer failure: {ex}");
        }
    }

    public Task NavigateAsync(string url)
    {
        ThrowIfDisposed();
        return Dispatcher.UIThread.InvokeAsync(() => _webView.Navigate(url)).GetTask();
    }

    public Task DeleteCookieAsync(string url, string name)
    {
        ThrowIfDisposed();
        return Dispatcher.UIThread.InvokeAsync(() =>
            _webView.DeleteCookiesNow(new[] { (url, name) })).GetTask();
    }

    public Task MirrorCookiesAsync(string sourceUrl, string targetDomain, string[] cookieNames)
    {
        ThrowIfDisposed();
        // CoreWebView2.CookieManager must be called on the UI thread it was created on.
        // We may be invoked from the threadpool (e.g. orchestrator continuation after the
        // user-confirm TCS resolves), so marshal explicitly. Avalonia's
        // `InvokeAsync(Func<Task>)` overload auto-unwraps the inner Task.
        return Dispatcher.UIThread.InvokeAsync(
            () => _webView.MirrorCookiesAsync(sourceUrl, targetDomain, cookieNames));
    }

    public Task<string?> GetCookiesAsync(string[] urls)
    {
        ThrowIfDisposed();
        // Same UI-thread requirement as MirrorCookiesAsync. Without this marshal, WebView2's
        // CookieManager throws a wrong-thread COM exception, our catch swallows it, and the
        // method returns null — which the controller mis-reports as "user cancelled".
        return Dispatcher.UIThread.InvokeAsync(() => _webView.GetCookiesAsync(urls));
    }

    public async Task WaitForUserConfirmAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (cancellationToken.CanBeCanceled)
        {
            await _userActionTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _userActionTcs.Task.ConfigureAwait(false);
        }
    }

    public void SetStatusText(string text)
    {
        if (_disposed) return;
        Dispatcher.UIThread.Post(() =>
        {
            // Re-check after marshalling — window may have closed.
            if (!_disposed) _window.SetStatusText(text);
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _userActionTcs.TrySetException(new OperationCanceledException("Session disposed."));

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => _window.Close()).GetTask().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AvaloniaWebViewSession] Dispose close failed: {ex}");
        }

        _handlerGate.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AvaloniaWebViewSession));
    }
}
