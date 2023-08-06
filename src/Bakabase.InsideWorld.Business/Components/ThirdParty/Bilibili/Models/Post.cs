using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models
{
    public class Post
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
        public Dimension Dimension { get; set; }
        public List<PostPage> Pages { get; set; }
        public Uploader Owner { get; set; }
    }
}