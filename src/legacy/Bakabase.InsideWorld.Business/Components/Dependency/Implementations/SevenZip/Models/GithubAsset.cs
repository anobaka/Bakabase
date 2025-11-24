using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip.Models
{
    public class GithubAsset
    {
        [JsonProperty("name")]
        public string Name { get; set; } = null!;

        [JsonProperty("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = null!;
    }
}
