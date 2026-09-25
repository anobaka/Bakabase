using System.Collections.Generic;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    /// <summary>The JSON behind a <c>subtitle_url</c>.</summary>
    public class SubtitleBody
    {
        /// <summary>
        /// Only AI subtitles carry it, and it is the real language: an <c>ai-zh</c> track was seen with
        /// <c>"lang": "th"</c>.
        /// </summary>
        public string? Lang { get; set; }

        public List<TLine>? Body { get; set; }

        public class TLine
        {
            /// <summary>Seconds.</summary>
            public double From { get; set; }

            /// <summary>Seconds.</summary>
            public double To { get; set; }

            public string? Content { get; set; }
            public int? Location { get; set; }
            public long? Sid { get; set; }
        }
    }
}
