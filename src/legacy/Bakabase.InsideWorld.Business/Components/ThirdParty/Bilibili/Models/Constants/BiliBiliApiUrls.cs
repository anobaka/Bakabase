using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models.Constants
{
    internal class BiliBiliApiConstants
    {
        public const string Session = "https://api.bilibili.com/x/space/v2/myinfo";

        public const string FavList =
            "https://api.bilibili.com/x/v3/fav/folder/created/list-all?up_mid={mid}&jsonp=jsonp";

        public static string FavItems =
            $"https://api.bilibili.com/x/v3/fav/resource/list?media_id={{mediaId}}&pn={{page}}&ps={FavPageSize}&keyword=&order=mtime&type=0&tid=0&platform=web&jsonp=jsonp";

        public const string PostInfo = "https://api.bilibili.com/x/web-interface/view?aid={aid}&jsonp=json";

        public const string VideoInfo =
            "https://api.bilibili.com/x/player/playurl?avid={aid}&cid={cid}&bvid=&qn=16&type=&otype=json&fourk=1&fnval=16";

        public const string PostPartPage = "https://www.bilibili.com/video/av{aid}?p={part}";

        public const string MoveFavResource = "https://api.bilibili.com/x/v3/fav/resource/move";

        public const int FavPageSize = 20;
    }
}