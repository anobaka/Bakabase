using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    public class VideoQuality
    {
        [JsonProperty("new_description")]
        public string Description { get; set; }
        public int Quality { get; set; }
    }
}