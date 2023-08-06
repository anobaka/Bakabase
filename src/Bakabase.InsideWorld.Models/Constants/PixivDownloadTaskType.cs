using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Constants
{
    public enum PixivDownloadTaskType
    {
        Search = 1,
        Ranking = 2,
        /// <summary>
        /// For https://www.pixiv.net/ajax/follow_latest/illust?p=x&mode=all only
        /// </summary>
        Following = 3
    }
}
