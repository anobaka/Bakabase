using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using Microsoft.Extensions.Options;

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
        private int _threadDebts;
        private DateTime _prevRequestDt;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly SemaphoreSlim _threadsSemaphore;

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
            // A zero or negative MaxConcurrency would create a semaphore that can never be entered,
            // wedging every request through this handler forever. Treat it as "one at a time".
            _threadsSemaphore = new SemaphoreSlim(NormalizeConcurrency(options.MaxConcurrency), int.MaxValue);
            ConfigureHandler();
        }

        /// <summary>
        /// Override to configure HttpClientHandler properties (e.g. AllowAutoRedirect).
        /// Called at the end of the constructor.
        /// </summary>
        protected virtual void ConfigureHandler()
        {
        }

        /// <summary>
        /// A concurrency of zero (or a negative value slipping in from a saved configuration) would
        /// make <see cref="_threadsSemaphore"/> permanently unenterable, so every request through
        /// this handler would hang forever with no error. Clamp instead.
        /// </summary>
        private static int NormalizeConcurrency(int maxConcurrency) => maxConcurrency < 1 ? 1 : maxConcurrency;

        protected TOptions Options
        {
            get => _options;
            set
            {
                var prevMaxThreads = NormalizeConcurrency(_options.MaxConcurrency);
                var nextMaxThreads = NormalizeConcurrency(value.MaxConcurrency);
                if (nextMaxThreads != prevMaxThreads)
                {
                    // Not the request gate: this only guards the debt bookkeeping below, which is why
                    // it is taken and released in one place rather than around a whole request.
                    _lock.Wait();

                    try
                    {
                        if (prevMaxThreads > nextMaxThreads)
                        {
                            _threadDebts += prevMaxThreads - nextMaxThreads;
                        }
                        else
                        {
                            _threadsSemaphore.Release(nextMaxThreads - prevMaxThreads);
                        }
                    }
                    finally
                    {
                        _lock.Release();
                    }
                }
                _options = value;
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

        private void WaitForInterval()
        {
            while (DateTime.Now < _prevRequestDt.AddMilliseconds(Options.RequestInterval))
            {
                Thread.Sleep(1);
            }
        }

        private async Task WaitForIntervalAsync(CancellationToken ct)
        {
            while (DateTime.Now < _prevRequestDt.AddMilliseconds(Options.RequestInterval))
            {
                await Task.Delay(1, ct);
            }
        }

        // Both request paths below acquire two semaphores and then run the request. The acquisitions
        // used to sit *outside* the try/finally that releases them, so anything throwing in between
        // — a cancellation landing after the gate was taken, or a malformed cookie/header making
        // BeforeRequesting throw — leaked the permit permanently. Because these handlers are
        // singletons and _lock is a 1-permit gate held for the whole request, one leak wedged every
        // later request to that source forever: downloads froze mid-step with no error and no way
        // back short of restarting the app. Track what was actually acquired and release exactly
        // that, whatever happens.

        protected sealed override HttpResponseMessage Send(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            WaitForInterval();

            var lockTaken = false;
            var threadTaken = false;

            try
            {
                _lock.Wait(cancellationToken);
                lockTaken = true;

                while (_threadDebts > 0)
                {
                    _threadsSemaphore.Wait(cancellationToken);
                    Interlocked.Decrement(ref _threadDebts);
                }

                _threadsSemaphore.Wait(cancellationToken);
                threadTaken = true;

                BeforeRequesting(request, cancellationToken);

                _prevRequestDt = DateTime.Now;
                var response = _logger.Capture(ThirdPartyId, () => base.Send(request, cancellationToken),
                    request.RequestUri?.ToString(), ct: cancellationToken);
                _processResponse(request, response);
                return response;
            }
            finally
            {
                if (threadTaken)
                {
                    _threadsSemaphore.Release();
                }

                if (lockTaken)
                {
                    _lock.Release();
                }
            }
        }

        protected sealed override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await WaitForIntervalAsync(cancellationToken);

            var lockTaken = false;
            var threadTaken = false;

            try
            {
                await _lock.WaitAsync(cancellationToken);
                lockTaken = true;

                while (_threadDebts > 0)
                {
                    await _threadsSemaphore.WaitAsync(cancellationToken);
                    Interlocked.Decrement(ref _threadDebts);
                }

                await _threadsSemaphore.WaitAsync(cancellationToken);
                threadTaken = true;

                await BeforeRequestingAsync(request, cancellationToken);

                _prevRequestDt = DateTime.Now;
                var response = await _logger.CaptureAsync(ThirdPartyId,
                    async () => await base.SendAsync(request, cancellationToken), request.RequestUri?.ToString(),
                    ct: cancellationToken);
                _processResponse(request, response);
                return response;
            }
            finally
            {
                if (threadTaken)
                {
                    _threadsSemaphore.Release();
                }

                if (lockTaken)
                {
                    _lock.Release();
                }
            }
        }
    }
}