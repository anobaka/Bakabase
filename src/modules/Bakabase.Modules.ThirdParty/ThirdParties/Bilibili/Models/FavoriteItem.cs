using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    public class FavoriteItem
    {
        public int Id { get; set; }
        [JsonProperty("bvid")]
        public string BvId { get; set; }
        public string Title { get; set; }
        public string Cover { get; set; }

        public Video.Upper Upper { get; set; }
    }
}