using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv.Models;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using CsQuery;
using JetBrains.Annotations;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv
{
    public class PixivClient : BakabaseHttpClient
    {
        public const string LoginStateCheckUrl = "https://www.pixiv.net/ajax/user/extra";
        protected override string HttpClientName => InternalOptions.HttpClientNames.Pixiv;

        public PixivClient(InsideWorldLocalizer localizer, IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory) : base(httpClientFactory, loggerFactory)
        {
        }

        public async Task<PixivIllustration> GetIllustrationInfo(string illustId)
        {
            var url = PixivUrls.IllustrationInfo.Replace("{id}", illustId);
            return (await GetJson<PixivBaseResponse<PixivIllustration>>(url)).Body;
        }

        public async Task<PixivRankingResponse> GetRankingData(string url) => await GetJson<PixivRankingResponse>(url);

        public async Task<byte[]> GetBytes(string url)
        {
            return await HttpClient.GetByteArrayAsync(url);
        }

        public async Task<T> GetJson<T>(string url)
        {
            var json = await HttpClient.GetStringAsync(url);
            var rsp = JsonConvert.DeserializeObject<T>(json);
            return rsp;
        }

        public async Task<PixivSearchResponse> Search(string url) =>
            (await GetJson<PixivBaseResponse<PixivSearchResponse>>(url)).Body;

        public async Task<FollowLatestIllustResponse> FollowLatestIllust(string url) =>
            (await GetJson<PixivBaseResponse<FollowLatestIllustResponse>>(url)).Body;
    }
}