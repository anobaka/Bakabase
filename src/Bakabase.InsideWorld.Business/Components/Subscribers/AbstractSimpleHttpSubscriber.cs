using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Subscribers
{
    public abstract class AbstractSimpleHttpSubscriber<TPost> : AbstractSubscriber<TPost>
    {
        protected abstract SystemPropertyKey CookieKey { get; }
        protected HttpClient HttpClient;
        protected abstract string UrlTemplate { get; }

        protected AbstractSimpleHttpSubscriber(IServiceProvider serviceProvider, ILoggerFactory loggerFactory) : base(
            serviceProvider, loggerFactory)
        {
        }

        protected override async Task<List<SubscriptionRecord>> GetRecords(SubscriptionDto subscription, int page,
            CancellationToken ct)
        {
            var url = string.Format(UrlTemplate, WebUtility.UrlEncode(subscription.Keyword), page);
            var htmlRsp = await HttpClient.GetAsync(url, ct);
            var html = await htmlRsp.Content.ReadAsStringAsync(ct);
            var list = ParseList(html);
            var data = new List<SubscriptionRecord>();
            foreach (var c in list)
            {
                var sd = await ParseData(c, ct);
                data.Add(sd);
            }

            return data;
        }

        protected abstract List<TPost> ParseList(string body);
        protected abstract Task<SubscriptionRecord> ParseData(TPost post, CancellationToken ct);

        protected override async Task Init(CancellationToken ct)
        {
            HttpClient = new HttpClient
            {
                DefaultRequestHeaders =
                {
                    { "User-Agent", (await SystemPropertyService.GetByKey(SystemPropertyKey.UserAgent, false))?.Value },
                    { "Cookie", (await SystemPropertyService.GetByKey(CookieKey, false))?.Value }
                }
            };
        }
    }
}