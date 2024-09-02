using System;
using System.Collections.Generic;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models.Constants;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    public class Video
    {
        public long Aid { get; set; }
        [JsonProperty("bvid")]
        public string BvId { get; set; }
        public long Cid { get; set; }
        public string Pic { get; set; }
        public string Title { get; set; }
        [JsonProperty("ctime")]
        public long CTime { get; set; }

        public DateTime CreateDt => new DateTime(1970, 1, 1).AddSeconds(CTime);
        public TDimension Dimension { get; set; }
        public List<TPage> Pages { get; set; }
        public Upper Owner { get; set; }

        public class Upper
        {
            public long Mid { get; set; }
            public string Name { get; set; }
            public string DirectoryName => $"{Mid}{BiliBiliCollectorConstants.FilePathDivider}{Name}";
        }

        public class TDimension
        {
            public int Width { get; set; }
            public int Height { get; set; }
        }

        public class TPage
        {
            public long Cid { get; set; }
            public int Page { get; set; }
            public TDimension Dimension { get; set; }
            public string Part { get; set; }

            public string FilenamePart => $"{Page}{BiliBiliCollectorConstants.FilePathDivider}{Part}";
        }
    }
}