namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models.Constants
{
    public partial class BiliBiliApiUrls
    {
        public const string Session = "https://api.bilibili.com/x/space/v2/myinfo";

        public const string FavList =
            "https://api.bilibili.com/x/v3/fav/folder/created/list-all?up_mid={mid}&jsonp=jsonp";

        /// <summary>
        /// <c>platform=web</c> is required: without it OGV items (type 24) are dropped from the list.
        /// </summary>
        public static string FavItems =
            $"https://api.bilibili.com/x/v3/fav/resource/list?media_id={{mediaId}}&pn={{page}}&ps={FavPageSize}&keyword=&order=mtime&type=0&tid=0&platform=web&jsonp=jsonp";

        public const string MoveFavResource = "https://api.bilibili.com/x/v3/fav/resource/move";

        public const int FavPageSize = 20;

        /// <summary>16|64|128|256|512|1024|2048 = DASH + HDR + 4K + Dolby audio + Dolby Vision + 8K + AV1.</summary>
        public const int PlayUrlFnval = 4048;

        /// <summary>The highest qn; playurl answers with the best tier the account may have.</summary>
        public const int DefaultPlayQn = 127;

        /// <summary>
        /// Legacy request kept ONLY for file-name compatibility (QualityName = the
        /// <c>new_description</c> of the highest <c>support_formats</c> entry of THIS answer, which never
        /// lists 125/126/127). Existing libraries are matched by that name, so never change this URL.
        /// </summary>
        public static string LegacyNamingPlayUrl(long aid, long cid) =>
            $"https://api.bilibili.com/x/player/playurl?avid={aid}&cid={cid}&bvid=&qn=16&type=&otype=json&fourk=1&fnval=16";

        public static string View(long aid) => $"https://api.bilibili.com/x/web-interface/view?aid={aid}";

        public static string PageList(long aid) => $"https://api.bilibili.com/x/player/pagelist?aid={aid}";

        public static string PlayUrl(long aid, long cid, int qn) =>
            $"https://api.bilibili.com/x/player/playurl?avid={aid}&cid={cid}&qn={qn}&fnval={PlayUrlFnval}&fnver=0&fourk=1&otype=json";

        public static string DmView(long aid, long cid) =>
            $"https://api.bilibili.com/x/v2/dm/view?type=1&oid={cid}&pid={aid}";

        /// <summary>Served raw-deflate compressed; decode with <c>BilibiliTextDecoder.DecodeDanmakuXml</c>.</summary>
        public static string DanmakuXml(long cid) => $"https://comment.bilibili.com/{cid}.xml";
    }
}
