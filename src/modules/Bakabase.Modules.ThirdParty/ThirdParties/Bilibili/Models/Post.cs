using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    /// <summary><c>x/web-interface/view</c> data.</summary>
    public class Post
    {
        public long Aid { get; set; }
        [JsonProperty("bvid")]
        public string? BvId { get; set; }
        public long Cid { get; set; }
        public string? Pic { get; set; }
        public string? Title { get; set; }
        [JsonProperty("ctime")]
        public long CTime { get; set; }

        public DateTime CreateDt => new DateTime(1970, 1, 1).AddSeconds(CTime);
        public Dimension? Dimension { get; set; }
        public List<PostPage>? Pages { get; set; }
        public Uploader? Owner { get; set; }

        /// <summary>Set for archives that are really a bangumi/film episode (<c>…/bangumi/play/ep…</c>).</summary>
        [JsonProperty("redirect_url")]
        public string? RedirectUrl { get; set; }

        /// <summary>The aid this archive was merged into (0 or missing when none).</summary>
        public long? Forward { get; set; }

        public int Videos { get; set; }

        /// <summary>Seconds.</summary>
        public long Duration { get; set; }

        public TRights? Rights { get; set; }

        [JsonProperty("is_upower_exclusive")]
        public bool IsUpowerExclusive { get; set; }

        [JsonProperty("is_upower_play")]
        public bool IsUpowerPlay { get; set; }

        [JsonProperty("is_upower_preview")]
        public bool IsUpowerPreview { get; set; }

        public class TRights
        {
            [JsonProperty("is_stein_gate")]
            public int IsSteinGate { get; set; }

            public int Pay { get; set; }

            [JsonProperty("ugc_pay")]
            public int UgcPay { get; set; }

            [JsonProperty("arc_pay")]
            public int ArcPay { get; set; }
        }
    }
}
