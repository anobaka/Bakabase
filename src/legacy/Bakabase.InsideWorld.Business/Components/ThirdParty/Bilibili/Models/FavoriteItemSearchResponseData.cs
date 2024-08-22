using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models
{
    public class FavoriteItemSearchResponseData
    {
        public Favorites Info { get; set; }
        public List<FavoriteItem> Medias { get; set; }
        [JsonProperty("has_more")]
        public bool HasMore { get; set; }
    }
}