using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    /// <summary><c>x/v2/dm/view</c> data (only the subtitle list is used).</summary>
    public class DmView
    {
        public TSubtitle? Subtitle { get; set; }

        public class TSubtitle
        {
            public List<TSubtitleItem>? Subtitles { get; set; }
        }

        public class TSubtitleItem
        {
            /// <summary>Language label, e.g. "zh-Hans", "en-US", "ai-zh". AI labels can be wrong.</summary>
            public string? Lan { get; set; }

            [JsonProperty("lan_doc")]
            public string? LanDoc { get; set; }

            /// <summary>Signed (<c>auth_key</c>) and short-lived: download it right away, never log it.</summary>
            [JsonProperty("subtitle_url")]
            public string? SubtitleUrl { get; set; }

            /// <summary>0 human, 1 AI.</summary>
            public int Type { get; set; }

            [JsonProperty("ai_type")]
            public int AiType { get; set; }

            [JsonProperty("ai_status")]
            public int AiStatus { get; set; }
        }
    }
}
