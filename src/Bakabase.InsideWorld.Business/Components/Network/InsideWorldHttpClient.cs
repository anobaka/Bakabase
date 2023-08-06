using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Resources;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Network
{
    public abstract class InsideWorldHttpClient
    {
        protected readonly InsideWorldLocalizer Localizer;
        private readonly IHttpClientFactory _httpClientFactory;
        private HttpClient? _httpClient;
        public HttpClient HttpClient => _httpClient ??= _httpClientFactory.CreateClient(HttpClientName);
        protected readonly ILogger Logger;
        protected abstract string HttpClientName { get; }

        protected InsideWorldHttpClient(InsideWorldLocalizer localizer, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
        {
            Localizer = localizer;
            _httpClientFactory = httpClientFactory;
            Logger = loggerFactory.CreateLogger(GetType());
        }
    }
}