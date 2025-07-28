using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.ThirdParty.Abstractions.Http
{
    public abstract class AbstractThirdPartyHttpMessageHandler<TOptions> : HttpClientHandler
        where TOptions : class, IThirdPartyHttpClientOptions, new()
    {
        private readonly ThirdPartyHttpRequestLogger _logger;
        private ThirdPartyId ThirdPartyId { get; }
        private int _threadDebts;
        private DateTime _prevRequestDt;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly SemaphoreSlim _threadsSemaphore;

        private TOptions _options;

        protected AbstractThirdPartyHttpMessageHandler(ThirdPartyHttpRequestLogger logger, ThirdPartyId thirdPartyId, BakabaseWebProxy webProxy, TOptions options)
        {
            _logger = logger;
            ThirdPartyId = thirdPartyId;
            _options = options;
            Proxy = webProxy;
            _threadsSemaphore = new SemaphoreSlim(options.MaxConcurrency, int.MaxValue);
        }

        protected TOptions Options
        {
            get => _options;
            set
            {
                var prevMaxThreads = _options.MaxConcurrency;
                if (value.MaxConcurrency != prevMaxThreads)
                {
                    _lock.Wait();

                    try
                    {
                        if (prevMaxThreads == value.MaxConcurrency)
                        {
                        }
                        else
                        {
                            if (prevMaxThreads > value.MaxConcurrency)
                            {
                                _threadDebts += prevMaxThreads - value.MaxConcurrency;
                            }
                            else
                            {
                                _threadsSemaphore.Release(value.MaxConcurrency - prevMaxThreads);
                            }
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

        private void _populateRequest(HttpRequestMessage request)
        {
            if (Options.UserAgent.IsNotEmpty())
            {
                request.Headers.UserAgent.Clear();
                request.Headers.Add("User-Agent",
                    Options.UserAgent ?? IThirdPartyHttpClientOptions.DefaultUserAgent);
            }

            if (Options.Cookie.IsNotEmpty())
            {
                request.Headers.Add("Cookie", Options.Cookie);
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

        protected sealed override HttpResponseMessage Send(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            WaitForInterval();
            _lock.Wait(cancellationToken);

            while (_threadDebts > 0)
            {
                _threadsSemaphore.Wait(cancellationToken);
                Interlocked.Decrement(ref _threadDebts);
            }

            _threadsSemaphore.Wait(cancellationToken);

            BeforeRequesting(request, cancellationToken);

            try
            {
                _prevRequestDt = DateTime.Now;
                return _logger.Capture(ThirdPartyId, () => base.Send(request, cancellationToken),
                    request.RequestUri?.ToString(), ct: cancellationToken);
            }
            finally
            {
                _threadsSemaphore.Release();
                _lock.Release();
            }
        }

        protected sealed override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await WaitForIntervalAsync(cancellationToken);
            await _lock.WaitAsync(cancellationToken);

            while (_threadDebts > 0)
            {
                await _threadsSemaphore.WaitAsync(cancellationToken);
                Interlocked.Decrement(ref _threadDebts);
            }

            await _threadsSemaphore.WaitAsync(cancellationToken);

            await BeforeRequestingAsync(request, cancellationToken);

            try
            {
                _prevRequestDt = DateTime.Now;
                return await _logger.CaptureAsync(ThirdPartyId,
                    async () => await base.SendAsync(request, cancellationToken), request.RequestUri?.ToString(),
                    ct: cancellationToken);
            }
            finally
            {
                _threadsSemaphore.Release();
                _lock.Release();
            }
        }
    }
}