using Microsoft.Extensions.Logging;

namespace Bakabase.Abstractions.Components.Network
{
    public abstract class BakabaseHttpClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private HttpClient? _httpClient;
        public HttpClient HttpClient => _httpClient ??= _httpClientFactory.CreateClient(HttpClientName);
        protected readonly ILogger Logger;
        protected abstract string HttpClientName { get; }

        protected BakabaseHttpClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
        {
            _httpClientFactory = httpClientFactory;
            Logger = loggerFactory.CreateLogger(GetType());
        }
    }
}