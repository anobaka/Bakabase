using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using System.Diagnostics;

namespace Bakabase.Modules.ThirdParty.Abstractions.Http
{
    /// <summary>
    /// Non-generic holder for HttpRequestMessage.Options keys used by third-party handlers.
    /// </summary>
    public static class ThirdPartyRequestOptions
    {
        /// <summary>
        /// Per-request account key. When set, the handler uses this instead of "default" for the cookie container.
        /// </summary>
        public static readonly HttpRequestOptionsKey<string> AccountKey = new("ThirdParty.AccountKey");

        /// <summary>
        /// Per-request cookie string. When set, the handler uses this instead of Options.Cookie.
        /// </summary>
        public static readonly HttpRequestOptionsKey<string> Cookie = new("ThirdParty.Cookie");
    }

    public abstract class AbstractThirdPartyHttpMessageHandler<TOptions> : HttpClientHandler
        where TOptions : class, IThirdPartyHttpClientOptions, new()
    {
        private readonly ThirdPartyHttpRequestLogger _logger;
        private readonly IThirdPartyCookieContainer? _cookieContainer;
        private ThirdPartyId ThirdPartyId { get; }
        private readonly object _requestGate = new();
        private int _activeRequests;
        private long? _lastRequestTimestamp;
        private TaskCompletionSource _stateChanged = NewStateChangedSignal();

        private TOptions _options;

        /// <summary>
        /// Cookie container key prefix for this handler. Used to scope containers per source.
        /// </summary>
        protected string CookieContainerKeyPrefix => $"{ThirdPartyId}:";

        protected AbstractThirdPartyHttpMessageHandler(ThirdPartyHttpRequestLogger logger, ThirdPartyId thirdPartyId, BakabaseWebProxy webProxy, TOptions options, IThirdPartyCookieContainer? cookieContainer = null)
        {
            _logger = logger;
            ThirdPartyId = thirdPartyId;
            _cookieContainer = cookieContainer;
            _options = options;
            // Bound to this source rather than using the global proxy directly, so a downloader can be
            // routed differently from the rest. Falls back to the global setting when it has no override.
            Proxy = webProxy.ForThirdParty(thirdPartyId);
            // Disable automatic cookie handling since we manage cookies manually via headers
            UseCookies = false;
            ConfigureHandler();
        }

        /// <summary>
        /// Override to configure HttpClientHandler properties (e.g. AllowAutoRedirect).
        /// Called at the end of the constructor.
        /// </summary>
        protected virtual void ConfigureHandler()
        {
        }

        // Invalid saved values must not make the request queue permanently unenterable.
        private static int NormalizeConcurrency(int maxConcurrency) => Math.Max(1, maxConcurrency);

        protected TOptions Options
        {
            get => Volatile.Read(ref _options);
            set
            {
                lock (_requestGate)
                {
                    Volatile.Write(ref _options, value);
                    NotifyStateChanged();
                }
            }
        }

        protected virtual void BeforeRequesting(HttpRequestMessage request, CancellationToken ct)
        {
            _populateRequest(request);
        }

        protected virtual Task BeforeRequestingAsync(HttpRequestMessage request, CancellationToken ct)
        {
            _populateRequest(request);
            return Task.CompletedTask;
        }

        private (string accountKey, string? cookie) GetRequestCookieInfo(HttpRequestMessage request)
        {
            request.Options.TryGetValue(ThirdPartyRequestOptions.AccountKey, out var accountKey);
            request.Options.TryGetValue(ThirdPartyRequestOptions.Cookie, out var cookie);
            return (accountKey ?? "default", cookie ?? Options.Cookie);
        }

        private void _populateRequest(HttpRequestMessage request)
        {
            if (Options.UserAgent.IsNotEmpty())
            {
                request.Headers.UserAgent.Clear();
                request.Headers.Add("User-Agent",
                    Options.UserAgent ?? IThirdPartyHttpClientOptions.DefaultUserAgent);
            }

            if (!request.Headers.Contains("Cookie"))
            {
                var (accountKey, cookie) = GetRequestCookieInfo(request);
                if (_cookieContainer != null && request.RequestUri != null)
                {
                    var cookieHeader = _cookieContainer.GetCookieHeader(
                        $"{CookieContainerKeyPrefix}{accountKey}", cookie, request.RequestUri);
                    if (cookieHeader != null)
                    {
                        request.Headers.Add("Cookie", cookieHeader);
                    }
                }
                else if (cookie.IsNotEmpty())
                {
                    request.Headers.Add("Cookie", cookie);
                }
            }

            if (Options.Referer.IsNotEmpty())
            {
                request.Headers.Add("Referer", Options.Referer);
            }

            if (Options.Headers != null)
            {
                foreach (var (k, v) in Options.Headers)
                {
                    request.Headers.Add(k, v);
                }
            }
        }

        private void _processResponse(HttpRequestMessage request, HttpResponseMessage response)
        {
            if (_cookieContainer != null && request.RequestUri != null)
            {
                var (accountKey, cookie) = GetRequestCookieInfo(request);
                _cookieContainer.ProcessResponse(
                    $"{CookieContainerKeyPrefix}{accountKey}", cookie, request.RequestUri, response);
            }
        }

        private static TaskCompletionSource NewStateChangedSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Call only under _requestGate. A changed limit or a completed request makes every
        // waiter recheck the current capacity and pacing; no stale semaphore debt is retained.
        private void NotifyStateChanged()
        {
            var previous = _stateChanged;
            _stateChanged = NewStateChangedSignal();
            previous.TrySetResult();
        }

        private async Task EnterRequestAsync(CancellationToken ct)
        {
            while (true)
            {
                Task changed;
                lock (_requestGate)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_activeRequests < NormalizeConcurrency(_options.MaxConcurrency))
                    {
                        _activeRequests++;
                        return;
                    }

                    changed = _stateChanged.Task;
                }

                await changed.WaitAsync(ct).ConfigureAwait(false);
            }
        }

        private async Task WaitForRequestStartAsync(CancellationToken ct)
        {
            while (true)
            {
                Task changed;
                TimeSpan delay;
                lock (_requestGate)
                {
                    ct.ThrowIfCancellationRequested();
                    delay = _lastRequestTimestamp is { } previous
                        ? TimeSpan.FromMilliseconds(Math.Max(0, _options.RequestInterval)) -
                          Stopwatch.GetElapsedTime(previous)
                        : TimeSpan.Zero;
                    if (delay <= TimeSpan.Zero)
                    {
                        _lastRequestTimestamp = Stopwatch.GetTimestamp();
                        return;
                    }

                    changed = _stateChanged.Task;
                }

                try
                {
                    await changed.WaitAsync(delay, ct).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // The interval elapsed. Recheck against the most recent request start.
                }
            }
        }

        private void ExitRequest()
        {
            lock (_requestGate)
            {
                _activeRequests--;
                NotifyStateChanged();
            }
        }

        protected sealed override HttpResponseMessage Send(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            EnterRequestAsync(cancellationToken).GetAwaiter().GetResult();
            try
            {
                BeforeRequesting(request, cancellationToken);
                WaitForRequestStartAsync(cancellationToken).GetAwaiter().GetResult();
                var response = _logger.Capture(ThirdPartyId, () => base.Send(request, cancellationToken),
                    request.RequestUri?.ToString(), ct: cancellationToken);
                _processResponse(request, response);
                return response;
            }
            finally
            {
                ExitRequest();
            }
        }

        protected sealed override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await EnterRequestAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await BeforeRequestingAsync(request, cancellationToken).ConfigureAwait(false);
                await WaitForRequestStartAsync(cancellationToken).ConfigureAwait(false);
                // Capacity covers the existing SendAsync boundary (response headers), not body reads.
                // Never hold the scheduling lock while the network is pending: slow headers must not
                // serialize every request regardless of MaxConcurrency.
                var response = await _logger.CaptureAsync(ThirdPartyId,
                    () => base.SendAsync(request, cancellationToken), request.RequestUri?.ToString(),
                    ct: cancellationToken).ConfigureAwait(false);
                _processResponse(request, response);
                return response;
            }
            finally
            {
                ExitRequest();
            }
        }
    }
}
