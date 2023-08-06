using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.ExHentai;
using Bakabase.InsideWorld.Business.Components.ExHentai.Models;
using Bakabase.InsideWorld.Business.Components.ExHentai.Models.RequestModels;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Subscribers.Implementations
{
    public class ExHentaiSubscriber : AbstractSubscriber<ExHentaiResource>
    {
        public override SubscriptionType Type => SubscriptionType.ExHentai;
        private readonly ExHentaiClient _exHentaiClient;
        protected override bool LocalizeCover => true;

        protected override async Task<byte[]> DownloadCover(string url)
        {
            var cookie = await SystemPropertyService.GetByKey(SystemPropertyKey.ExHentaiCookie, true);
            return await _exHentaiClient.DownloadImage(cookie?.Value, url);
        }

        public ExHentaiSubscriber(IServiceProvider serviceProvider, ILoggerFactory loggerFactory,
            ExHentaiClient exHentaiClient) : base(serviceProvider, loggerFactory)
        {
            _exHentaiClient = exHentaiClient;
        }

        protected override async Task<List<SubscriptionRecord>> GetRecords(SubscriptionDto subscription, int page,
            CancellationToken ct)
        {
            var cookie = await SystemPropertyService.GetByKey(SystemPropertyKey.ExHentaiCookie, true);
            var resources = (await _exHentaiClient.Search(cookie?.Value, new ExHentaiSearchRequestModel
            {
                PageIndex = page - 1,
                Keyword = subscription.Keyword
            })).Data;
            return resources.Select(r => new SubscriptionRecord
            {
                CoverUrl = r.CoverUrl,
                DownloadUrl = r.TorrentPageUrl,
                Name = r.Name,
                PublishDt = r.UpdateDt,
                SubscriptionId = subscription.Id,
                Url = r.Url
            }).ToList();
        }
    }
}