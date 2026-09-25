using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    public class FavoriteItemSearchResponseData
    {
        public ApiFavorites? Info { get; set; }

        /// <summary>
        /// Never null once returned by <c>BilibiliClient.GetPostsInFavorites</c> (Bilibili sends
        /// <c>"medias": null</c> for empty pages).
        /// </summary>
        public List<FavoriteItem>? Medias { get; set; }

        [JsonProperty("has_more")]
        public bool HasMore { get; set; }
    }
}
