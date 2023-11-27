using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.Extensions.Localization;
using System.Threading;
using Bakabase.InsideWorld.Business.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using NPOI.XSSF.Model;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models.Constants;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models;
using Bakabase.InsideWorld.Business.Resources;
using Microsoft.Extensions.Logging;
using Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili
{
    public class BilibiliClient : InsideWorldHttpClient
    {
        public BilibiliClient(InsideWorldLocalizer localizer, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory) : base(localizer, httpClientFactory, loggerFactory)
        {
        }

        protected override string HttpClientName => BusinessConstants.HttpClientNames.Bilibili;

        public async Task<List<Favorites>> GetFavorites()
        {
            var userCredentialRsp = await HttpClient.GetStringAsync(BiliBiliApiConstants.Session);
            var userCredential = JsonConvert.DeserializeObject<DataWrapper<UserCredential>>(userCredentialRsp)?.Data;
            var mid = userCredential?.Profile.Mid;
            if (!mid.HasValue)
            {
                throw new Exception(Localizer[SharedResource.Downloader_BilibiliCookieIsInvalid]);
            }

            var favListJson =
                await HttpClient.GetStringAsync(BiliBiliApiConstants.FavList.Replace("{mid}", mid.ToString()));
            var favList = JsonConvert.DeserializeObject<DataWrapper<FavoritesList>>(favListJson).Data.List;
            return favList;
        }

        public async Task<FavoriteItemSearchResponseData> GetPostsInFavorites(long favoritesId, int page,
            CancellationToken ct)
        {
            var url = BiliBiliApiConstants.FavItems.Replace("{mediaId}", favoritesId.ToString())
                .Replace("{page}", page.ToString());
            var postsJson = await HttpClient.GetStringAsync(url, ct);
            var rsp = JsonConvert.DeserializeObject<DataWrapper<FavoriteItemSearchResponseData>>(postsJson);
            return rsp.Data;
        }

        public async Task<Post> GetPostInfo(long aid, CancellationToken ct)
        {
            var postUrl = BiliBiliApiConstants.PostInfo.Replace("{aid}", aid.ToString());
            // 上面的请求可以获取page总数，但还需要page名称，所以需要深层请求
            var postJson = await HttpClient.GetStringAsync(postUrl, ct);
            var result = JsonConvert.DeserializeObject<DataWrapper<Post>>(postJson);
            if (result.Code != 0)
            {

            }

            var postInfo = result.Data;
            return postInfo;
        }

        public async Task<VideoSource> GetVideoSource(long aid, long cid, CancellationToken ct)
        {
            var videoUrl = BiliBiliApiConstants.VideoInfo.Replace("{aid}", aid.ToString())
                .Replace("{cid}", cid.ToString());
            var videoSourceJson = await HttpClient.GetStringAsync(videoUrl, ct);
            var videoSource =
                JsonConvert.DeserializeObject<DataWrapper<VideoSource>>(videoSourceJson!)!.Data;
            return videoSource;
        }

        //public async Task<BaseResponse> MoveFavResources(string cookie, int srcFavId, int dstFavId,
        //    CancellationToken ct)
        //{
        //    if (srcFavId == dstFavId)
        //    {
        //        return BaseResponseBuilder.BuildBadRequest(
        //            $"{nameof(srcFavId)} can not be equal to {nameof(dstFavId)}");
        //    }

        //    var favorites = await GetFavorites(cookie);
        //    var srcFav = favorites.FirstOrDefault(t => t.Id == srcFavId);
        //    if (srcFav == null)
        //    {
        //        return BaseResponseBuilder.BuildBadRequest($"Can not find source favorites with id: {srcFavId}");
        //    }

        //    var dstFav = favorites.FirstOrDefault(t => t.Id == dstFavId);
        //    if (dstFav == null)
        //    {
        //        return BaseResponseBuilder.BuildBadRequest($"Can not find source favorites with id: {dstFavId}");
        //    }

        //    while (true)
        //    {
        //        var url = BiliBiliApiUrls.FavItems.Replace("{mediaId}", srcFavId.ToString()).Replace("{page}", "1");
        //        var postsJson = await _httpClient.GetStringAsync(url, ct);
        //        var favResourcesRsp = JsonConvert
        //            .DeserializeObject<DataWrapper<FavoriteItemSearchResponseData>>(postsJson).Data;

        //        await Task.Delay(1000, ct);

        //        if (!favResourcesRsp.HasMore && favResourcesRsp.Medias?.Any() != true)
        //        {
        //            break;
        //        }

        //        var resourceIds = favResourcesRsp.Medias.Select(t => t.Id).ToArray();
        //        await MoveFavResources(cookie, srcFavId, dstFavId, resourceIds, ct);
        //    }

        //    return BaseResponseBuilder.Ok;
        //}

        //private async Task MoveFavResources(string cookie, int srcFavId, int dstFavId, int[] resourceIds,
        //    CancellationToken ct)
        //{
        //    var csrf = cookie.Split(';').Select(t => t.Trim()).Select(t => t.Split('='))
        //        .FirstOrDefault(t => t[0] == "bili_jct")?[1];

        //    if (csrf.IsNullOrEmpty())
        //    {
        //        throw new Exception("Can not find bili_jct in cookie");
        //    }

        //    var requestValuesTemplate = new List<(string, object)>
        //    {
        //        ("src_media_id", srcFavId),
        //        ("tar_media_id", dstFavId),
        //        ("platform", "web"),
        //        ("jsonp", "jsonp"),
        //        ("csrf", csrf),
        //    };
        //    var requestValues = requestValuesTemplate.ToList();
        //    requestValues.Add(("resources", string.Join(',', resourceIds.Select(t => $"{t}:2"))));
        //    var sourceClient = _httpClient;
        //    var rsp = await sourceClient.PostAsync(BiliBiliApiUrls.MoveFavResource,
        //        new FormUrlEncodedContent(requestValues.Select(t =>
        //            new KeyValuePair<string, string>(t.Item1, t.Item2?.ToString()))), ct);
        //    if (rsp.IsSuccessStatusCode)
        //    {
        //        var body = await rsp.Content.ReadAsStringAsync(ct);
        //        var jsonData = JsonConvert.DeserializeObject<DataWrapper<int>>(body);
        //        if (jsonData.Code == 0 && jsonData.Data == 0)
        //        {
        //            return;
        //        }

        //        throw new Exception($"Bad bilibili response: {body}");
        //    }
        //    else
        //    {
        //        throw new Exception($"Bad bilibili response: ({(int)rsp.StatusCode}){rsp.StatusCode}");
        //    }
        //}
    }
}