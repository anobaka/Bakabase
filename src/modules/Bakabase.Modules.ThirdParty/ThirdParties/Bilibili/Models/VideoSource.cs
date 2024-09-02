using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    public class VideoSource
    {
        // [JsonProperty("accept_quality")]
        // public int AcceptQuality { get; set; }
        public TDash Dash { get; set; }
        [JsonProperty("support_formats")]
        public List<VideoQuality> SupportFormats { get; set; }

        public class TDash
        {
            public List<TFragement> Audio { get; set; }
            public List<TFragement> Video { get; set; }
            public class TFragement
            {
                public int Bandwidth { get; set; }
                [JsonProperty("base_url")]
                public string BaseUrl { get; set; }
                [JsonProperty("codecid")]
                public int CodecId { get; set; }
                // QualityId
                public int Id { get; set; }
            }
        }
    }
}