using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models
{
    public class VideoQuality
    {
        [JsonProperty("new_description")]
        public string Description { get; set; }
        public int Quality { get; set; }
    }
}