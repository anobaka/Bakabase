using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv.Models
{
    public class PixivUrls
    {
        public const string IllustrationInfo = "https://www.pixiv.net/ajax/illust/{id}";

        public const string IllustrationsOfFollowingUsersByLatest =
            "https://www.pixiv.net/ajax/follow_latest/illust?p={page}&mode=all";
    }
}
