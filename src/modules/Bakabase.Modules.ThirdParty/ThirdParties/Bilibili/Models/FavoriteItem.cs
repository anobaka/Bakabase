using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    /// <summary>
    /// One entry of <c>x/v3/fav/resource/list</c>. What <see cref="Id"/> means depends on
    /// <see cref="Type"/> (2: an aid, 12: an audio id, 24: an OGV ep_id, 21: a season id), so an item must
    /// be classified (<c>BilibiliFavoriteItemClassifier</c>) before any request is made for it.
    /// </summary>
    public class FavoriteItem
    {
        public long Id { get; set; }

        /// <summary>2 video, 12 audio, 21 UGC season (collection), 24 OGV episode; 0 when the field is missing.</summary>
        public int Type { get; set; }

        /// <summary>
        /// Bit mask: 1 invalid (removed / invalidated), 2 PGC archive, 4 login-only legacy archive,
        /// 16 interactive video.
        /// </summary>
        public int Attr { get; set; }

        [JsonProperty("bvid")]
        public string? BvId { get; set; }

        public string? Title { get; set; }
        public string? Cover { get; set; }

        /// <summary>Number of pages the list claims (may be 0 for invalid items).</summary>
        public int Page { get; set; }

        /// <summary>Seconds.</summary>
        public int Duration { get; set; }

        public string? Link { get; set; }

        public Video.Upper? Upper { get; set; }
        public TUgc? Ugc { get; set; }
        public TOgv? Ogv { get; set; }

        public class TUgc
        {
            [JsonProperty("first_cid")]
            public long FirstCid { get; set; }
        }

        public class TOgv
        {
            [JsonProperty("season_id")]
            public long SeasonId { get; set; }

            [JsonProperty("type_name")]
            public string? TypeName { get; set; }
        }
    }
}
