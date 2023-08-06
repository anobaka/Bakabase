using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models.Constants
{
    internal class BiliBiliApiConstants
    {
        public const string Session =
            "https://passport.bilibili.com/login/app/third?appkey=27eb53fc9058f8c3&api=https%3A%2F%2Fwww.mcbbs.net%2Ftemplate%2Fmcbbs%2Fimage%2Fspecial_photo_bg.png&sign=04224646d1fea004e79606d3b038c84a";

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