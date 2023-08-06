using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv.Models.PixivSearchResponse;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv.Models
{
    public class PixivSearchResponse
    {
        public ListData Novel { get; set; }
        public List<string> RelatedTags { get; set; }
        public Dictionary<string, Dictionary<string, string>> TagTranslation { get; set; }
        public ListData IllustManga { get; set; }
        public ListData Illust { get; set; }
        public ListData Manga { get; set; }

        public class ListData
        {
            public List<Work> Data { get; set; }

            public class Work
            {
                public string Id { get; set; }
                public string Title { get; set; }
                public int IllustType { get; set; }
                public int XRestrict { get; set; }
                public int Restrict { get; set; }
                public int Sl { get; set; }
                public string Url { get; set; }
                public string Description { get; set; }
                public List<string> Tags { get; set; }
                public string UserId { get; set; }
                public string UserName { get; set; }
                public int Width { get; set; }
                public int Height { get; set; }
                public int PageCount { get; set; }
                public bool IsBookmarkable { get; set; }
                public object BookmarkData { get; set; }
                public string Alt { get; set; }
                public DateTime CreateDate { get; set; }
                public DateTime UpdateDate { get; set; }
                public bool IsUnlisted { get; set; }
                public bool IsMasked { get; set; }
                public int AiType { get; set; }
                public string ProfileImageUrl { get; set; }
            }

            public int Total { get; set; }
        }
    }
}