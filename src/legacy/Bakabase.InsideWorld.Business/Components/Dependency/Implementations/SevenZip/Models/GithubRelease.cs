using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip.Models
{
    public class GithubRelease
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; } = null!;

        [JsonProperty("name")]
        public string Name { get; set; } = null!;

        [JsonProperty("body")]
        public string Body { get; set; } = null!;

        [JsonProperty("assets")]
        public List<GithubAsset> Assets { get; set; } = null!;
    }
}
