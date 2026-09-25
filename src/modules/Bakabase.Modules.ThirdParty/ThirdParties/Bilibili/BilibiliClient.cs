using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.Components.Localization;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Extensions;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili
{
    /// <summary>
    /// Bilibili API client (cookie, 1 request/s through the named "Bilibili" client). API endpoints only: CDN,
    /// danmaku, subtitle and cover downloads use the cookie-less CDN client.
    /// </summary>
    /// <remarks>
    /// Every GET goes through <see cref="GetApiAsync{T}"/>, which throws for risk control and busy answers
    /// (<see cref="BilibiliTemporarilyUnavailableException"/>, transient), -101
    /// (<see cref="BilibiliNotLoggedInException"/>), unknown codes (<see cref="BilibiliApiException"/>) and
    /// malformed bodies (<see cref="BilibiliProtocolException"/>), and returns every content-state code for the
    /// decision tables in <c>Protocol/</c> to interpret. Exceptions carry endpoint names and codes only — never a
    /// URL, a cookie or a body.
    /// </remarks>
    public partial class BilibiliClient : BakabaseHttpClient
    {
        public const string MyInfoEndpoint = "myinfo";
        public const string FavoritesEndpoint = "favorites";
        public const string FavoriteItemsEndpoint = "favorites-items";
        public const string ViewEndpoint = BilibiliArchiveRules.ViewEndpoint;
        public const string PageListEndpoint = BilibiliArchiveRules.PageListEndpoint;
        public const string NamingEndpoint = "playurl-naming";
        public const string PlayUrlEndpoint = BilibiliPlayUrlRules.PlayUrlEndpoint;
        public const string DmViewEndpoint = "dm-view";

        private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None,
        });

        private readonly IThirdPartyLocalizer _localizer;

        public BilibiliClient(IThirdPartyLocalizer localizer, IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory) : base(httpClientFactory, loggerFactory)
        {
            _localizer = localizer;
        }

        protected override string HttpClientName => InternalOptions.HttpClientNames.Bilibili;

        /// <summary>The logged-in account's own folders.</summary>
        /// <exception cref="BilibiliNotLoggedInException">The cookie is not logged in.</exception>
        public async Task<List<Favorites>> GetFavorites(CancellationToken ct = default)
        {
            var mid = await EnsureLoggedIn(ct);
            var list = await GetApiAsync<FavoritesList>(FavoritesEndpoint,
                BiliBiliApiUrls.FavList.Replace("{mid}", mid.ToString()), ct);
            EnsureOk(FavoritesEndpoint, list);
            return (list.Data?.List ?? []).Select(f => f.ToDomain()).ToList();
        }

        /// <summary>
        /// myinfo: the logged-in account's mid. Logged-out calls to view and playurl still answer code 0 (with
        /// lower qualities), so this is the one reliable login check.
        /// </summary>
        /// <exception cref="BilibiliNotLoggedInException">The cookie is not logged in.</exception>
        public async Task<long> EnsureLoggedIn(CancellationToken ct)
        {
            var me = await GetApiAsync<UserCredential>(MyInfoEndpoint, BiliBiliApiUrls.Session, ct);
            if (me.Code != BilibiliApiCodes.Ok || me.Data?.Profile is not { Mid: > 0 } profile)
            {
                throw new BilibiliNotLoggedInException(_localizer.ThirdParty_Bilibili_CookieIsInvalid());
            }

            return profile.Mid;
        }

        /// <summary>One page of a folder. <see cref="FavoriteItemSearchResponseData.Medias"/> is never null.</summary>
        public async Task<FavoriteItemSearchResponseData> GetPostsInFavorites(long favoritesId, int page,
            CancellationToken ct)
        {
            var url = BiliBiliApiUrls.FavItems.Replace("{mediaId}", favoritesId.ToString())
                .Replace("{page}", page.ToString());
            var rsp = await GetApiAsync<FavoriteItemSearchResponseData>(FavoriteItemsEndpoint, url, ct);
            EnsureOk(FavoriteItemsEndpoint, rsp);
            var data = rsp.Data ?? throw new BilibiliProtocolException(FavoriteItemsEndpoint, "code 0 without data");
            data.Medias ??= [];
            return data;
        }

        /// <summary><c>x/web-interface/view</c>; interpret with <see cref="BilibiliArchiveRules.DecideView"/>.</summary>
        public Task<DataWrapper<Post>> GetView(long aid, CancellationToken ct) =>
            GetApiAsync<Post>(ViewEndpoint, BiliBiliApiUrls.View(aid), ct);

        /// <summary><c>x/player/pagelist</c>; interpret with <see cref="BilibiliArchiveRules"/>.</summary>
        public Task<DataWrapper<List<PostPage>>> GetPageList(long aid, CancellationToken ct) =>
            GetApiAsync<List<PostPage>>(PageListEndpoint, BiliBiliApiUrls.PageList(aid), ct);

        /// <summary>
        /// The legacy fnval=16 playurl request, used ONLY for the QualityName of file names
        /// (<see cref="BilibiliQualityNaming.LegacyQualityName"/>).
        /// </summary>
        public Task<DataWrapper<VideoSource>> GetLegacyNamingSource(long aid, long cid, CancellationToken ct) =>
            GetApiAsync<VideoSource>(NamingEndpoint, BiliBiliApiUrls.LegacyNamingPlayUrl(aid, cid), ct);

        /// <summary>playurl with fnval=4048; interpret with <see cref="BilibiliPlayUrlRules.Decide"/>.</summary>
        public Task<DataWrapper<VideoSource>> GetPlayUrl(long aid, long cid, int qn, CancellationToken ct) =>
            GetApiAsync<VideoSource>(PlayUrlEndpoint, BiliBiliApiUrls.PlayUrl(aid, cid, qn), ct);

        /// <summary><c>x/v2/dm/view</c> (subtitle list). Code ≠ 0 or <c>subtitle: null</c> means no subtitles.</summary>
        public Task<DataWrapper<DmView>> GetDmView(long aid, long cid, CancellationToken ct) =>
            GetApiAsync<DmView>(DmViewEndpoint, BiliBiliApiUrls.DmView(aid, cid), ct);

        /// <summary>
        /// The single choke point for every API GET (future WBI signing / dm_img_* parameters go here).
        /// </summary>
        private async Task<DataWrapper<T>> GetApiAsync<T>(string endpoint, string url, CancellationToken ct)
        {
            using var rsp = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);
            var status = (int) rsp.StatusCode;
            switch (BilibiliApiCodes.ClassifyApiHttpStatus(rsp.StatusCode))
            {
                case BilibiliApiCodeClass.Ok:
                    break;
                case BilibiliApiCodeClass.RiskControl:
                    Logger.LogWarning("Bilibili {Endpoint} refused the request with HTTP {Status} (risk control)",
                        endpoint, status);
                    throw new BilibiliTemporarilyUnavailableException(BilibiliTemporaryFailureKind.RiskControl,
                        endpoint, null, httpStatus: status);
                case BilibiliApiCodeClass.ServiceBusy:
                    throw new BilibiliTemporarilyUnavailableException(BilibiliTemporaryFailureKind.ServiceBusy,
                        endpoint, null, httpStatus: status);
                default:
                    throw new BilibiliProtocolException(endpoint, $"HTTP {status}");
            }

            var body = await rsp.Content.ReadAsStringAsync(ct);

            JObject root;
            try
            {
                using var reader = new JsonTextReader(new StringReader(body)) {DateParseHandling = DateParseHandling.None};
                root = JToken.ReadFrom(reader) as JObject ?? throw new JsonReaderException("not an object");
            }
            catch (JsonException)
            {
                LogUnexpectedBody(endpoint, "not JSON", body);
                // No inner exception: a JSON exception's message quotes the offending text.
                throw new BilibiliProtocolException(endpoint, "response is not a JSON object");
            }

            int code;
            try
            {
                code = root.Value<int?>("code") ??
                       throw new BilibiliProtocolException(endpoint, "response has no code");
            }
            catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
            {
                LogUnexpectedBody(endpoint, "code is not a number", body);
                throw new BilibiliProtocolException(endpoint, "code is not a number");
            }

            var voucher = (root["data"] as JObject)?["v_voucher"]?.ToString();
            var message = root["message"]?.Type == JTokenType.String ? root.Value<string>("message") : null;
            switch (BilibiliApiCodes.Classify(code, voucher))
            {
                case BilibiliApiCodeClass.RiskControl:
                    Logger.LogWarning("Bilibili {Endpoint} answered code {Code} (risk control{Voucher})", endpoint,
                        code, string.IsNullOrEmpty(voucher) ? "" : ", captcha requested");
                    throw new BilibiliTemporarilyUnavailableException(BilibiliTemporaryFailureKind.RiskControl,
                        endpoint, code);
                case BilibiliApiCodeClass.ServiceBusy:
                    throw new BilibiliTemporarilyUnavailableException(BilibiliTemporaryFailureKind.ServiceBusy,
                        endpoint, code);
                case BilibiliApiCodeClass.NotLoggedIn:
                    throw new BilibiliNotLoggedInException(_localizer.ThirdParty_Bilibili_CookieIsInvalid());
                case BilibiliApiCodeClass.Unknown:
                    Logger.LogWarning("Bilibili {Endpoint} answered the unknown code {Code}", endpoint, code);
                    throw new BilibiliApiException(endpoint, code, message);
            }

            try
            {
                return root.ToObject<DataWrapper<T>>(Serializer) ??
                       throw new BilibiliProtocolException(endpoint, "empty response");
            }
            catch (JsonException e)
            {
                var path = e switch
                {
                    JsonSerializationException jse => jse.Path,
                    JsonReaderException jre => jre.Path,
                    _ => null,
                };
                LogUnexpectedBody(endpoint, $"unexpected shape at {path}", body);
                throw new BilibiliProtocolException(endpoint, $"unexpected shape at {SafePath(path)}");
            }
        }

        private void LogUnexpectedBody(string endpoint, string problem, string body) =>
            Logger.LogDebug("Bilibili {Endpoint}: {Problem}. Redacted body: {Body}", endpoint, problem,
                BilibiliDiagnostics.RedactJson(body));

        /// <summary>A JSON path is built from property names and indexes only; still, keep it short and plain.</summary>
        private static string SafePath(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "(root)";
            }

            var clean = new string(path.Where(c => char.IsLetterOrDigit(c) || c is '.' or '[' or ']' or '_').ToArray());
            return clean.Length <= 100 ? clean : clean[..100];
        }

        private static void EnsureOk<T>(string endpoint, DataWrapper<T> rsp)
        {
            if (rsp.Code != BilibiliApiCodes.Ok)
            {
                throw new BilibiliApiException(endpoint, rsp.Code, rsp.Message);
            }
        }
    }
}
