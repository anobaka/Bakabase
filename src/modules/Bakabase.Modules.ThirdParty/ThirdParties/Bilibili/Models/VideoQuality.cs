using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    /// <summary>One entry of playurl <c>support_formats</c>.</summary>
    public class VideoQuality
    {
        /// <summary>
        /// <c>new_description</c>, e.g. "1080P 高清". Part of downloaded file names (QualityName), so the
        /// field it binds to must never change.
        /// </summary>
        [JsonProperty("new_description")]
        public string? Description { get; set; }

        public int Quality { get; set; }

        public List<string>? Codecs { get; set; }
    }
}
