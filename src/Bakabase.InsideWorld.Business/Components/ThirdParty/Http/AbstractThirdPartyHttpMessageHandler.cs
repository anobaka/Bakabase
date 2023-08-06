using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bootstrap.Extensions;
using JetBrains.Annotations;
using Microsoft.Extensions.Options;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Http
{
    public abstract class AbstractThirdPartyHttpMessageHandler<TOptions> : HttpClientHandler
        where TOptions : ThirdPartyHttpClientOptions
    {
        private readonly ThirdPartyHttpRequestLogger _logger;
        public ThirdPartyId ThirdPartyId { get; }
        private int _threadDebts;
        private DateTime _prevRequestDt;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly SemaphoreSlim _threadsSemaphore = new(0, int.MaxValue);

        private IOptions<TOptions> _options;

        protected AbstractThirdPartyHttpMessageHandler(ThirdPartyHttpRequestLogger logger, ThirdPartyId thirdPartyId, InsideWorldWebProxy webProxy)
        {
            _logger = logger;
            ThirdPartyId = thirdPartyId;
            Proxy = webProxy;
        }

        public virtual IOptions<TOptions> Options
        {
            protected get => _options;
            set
            {
                if (value?.Value != null)
                {
                    var prevMaxThreads = _options?.Value?.MaxThreads ?? 0;
                    if (value.Value.MaxThreads != prevMaxThreads)
                    {
                        _lock.Wait();

                        try
                        {
                            if (prevMaxThreads == value.Value.MaxThreads)
                            {
                            }
                            else
                            {
                                if (prevMaxThreads > value.Value.MaxThreads)
                                {
                                    _threadDebts += prevMaxThreads - value.Value.MaxThreads;
                                }
                                else
                                {
                                    _threadsSemaphore.Release(value.Value.MaxThreads - prevMaxThreads);
                                }
                            }
                        }
                        finally
                        {
                            _lock.Release();
                        }
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
            if (Options.Value.UserAgent.IsNotEmpty())
            {
                request.Headers.UserAgent.Clear();
                request.Headers.Add("User-Agent",
                    Options.Value.UserAgent ?? ThirdPartyHttpClientOptions.DefaultUserAgent);
            }

            if (Options.Value.Cookie.IsNotEmpty())
            {
                request.Headers.Add("Cookie", Options.Value.Cookie);
            }

            if (Options.Value.Referer.IsNotEmpty())
            {
                request.Headers.Add("Referer", Options.Value.Referer);
            }

            if (Options.Value.Headers != null)
            {
                foreach (var (k, v) in Options.Value.Headers)
                {
                    request.Headers.Add(k, v);
                }
            }
        }

        private void WaitForInterval()
        {
            while (DateTime.Now < _prevRequestDt.AddMilliseconds(Options.Value.Interval))
            {
                Thread.Sleep(1);
            }
        }

        private async Task WaitForIntervalAsync(CancellationToken ct)
        {
            while (DateTime.Now < _prevRequestDt.AddMilliseconds(Options.Value.Interval))
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