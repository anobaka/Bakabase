using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services.Bootstraps;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.SystemProperty;
using ProxyProvider.HttpClientProvider;

namespace Bakabase.InsideWorld.Business.Components.HttpClientProvider
{
    public class SystemPropertyCookieHttpClientProvider : IHttpClientProvider
    {
        private readonly SystemPropertyService _systemPropertyService;

        public SystemPropertyCookieHttpClientProvider(SystemPropertyService systemPropertyService)
        {
            _systemPropertyService = systemPropertyService;
        }

        private readonly ConcurrentDictionary<SystemPropertyKey, HttpClient> _clients = new();

        public Task<HttpClient> GetClient(string purpose = null)
        {
            if (Enum.TryParse<SystemPropertyKey>(purpose, out var type))
            {
                var client = _clients.GetOrAdd(type, t =>
                {
                    var cookie = _systemPropertyService.GetByKey(type, false);
                    const string ua =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/80.0.3987.163 Safari/537.36";
                    var cc = new CookieContainer();
                    cc.SetCookies(new Uri(".bilibili.com"), $"Set-Cookie: {cookie}");
                    cc.Add(new Cookie());
                    var c = new HttpClient(new HttpClientHandler {CookieContainer = cc});
                    c.DefaultRequestHeaders.UserAgent.ParseAdd(ua);
                    return c;
                });
                return Task.FromResult(client);
            }

            throw new ArgumentOutOfRangeException(nameof(purpose), "Can not be converted to SystemPropertyKey");
        }
    }
}