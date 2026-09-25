using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    /// <summary><c>x/player/playurl</c> data.</summary>
    public class VideoSource
    {
        /// <summary>Echoes the requested qn rather than what was delivered; do not rank streams by it.</summary>
        public int Quality { get; set; }

        public string? Format { get; set; }

        /// <summary>Milliseconds.</summary>
        public long Timelength { get; set; }

        [JsonProperty("accept_quality")]
        public List<int>? AcceptQuality { get; set; }

        public TDash? Dash { get; set; }

        /// <summary>Legacy progressive segments (only when no DASH is offered).</summary>
        public List<TDurl>? Durl { get; set; }

        [JsonProperty("support_formats")]
        public List<VideoQuality>? SupportFormats { get; set; }

        public class TDash
        {
            /// <summary>Seconds.</summary>
            public long Duration { get; set; }

            public List<TFragement>? Audio { get; set; }
            public List<TFragement>? Video { get; set; }
            public TDolby? Dolby { get; set; }
            public TFlac? Flac { get; set; }

            public class TFragement
            {
                public long Bandwidth { get; set; }

                [JsonProperty("base_url")]
                public string? BaseUrl { get; set; }

                [JsonProperty("backup_url")]
                public List<string>? BackupUrl { get; set; }

                [JsonProperty("codecid")]
                public int CodecId { get; set; }

                /// <summary>Quality id for video (e.g. 80), stream id for audio (e.g. 30280).</summary>
                public int Id { get; set; }

                public string? Codecs { get; set; }

                [JsonProperty("mime_type")]
                public string? MimeType { get; set; }

                public int Width { get; set; }
                public int Height { get; set; }

                [JsonProperty("frame_rate")]
                public string? FrameRate { get; set; }
            }

            public class TDolby
            {
                public int Type { get; set; }

                /// <summary>An <b>array</b> (may be null).</summary>
                public List<TFragement>? Audio { get; set; }
            }

            public class TFlac
            {
                public bool Display { get; set; }

                /// <summary>A <b>single object</b> (may be null).</summary>
                public TFragement? Audio { get; set; }
            }
        }

        public class TDurl
        {
            public int Order { get; set; }

            /// <summary>Milliseconds.</summary>
            public long Length { get; set; }

            public long Size { get; set; }
            public string? Url { get; set; }

            [JsonProperty("backup_url")]
            public List<string>? BackupUrl { get; set; }
        }
    }
}
