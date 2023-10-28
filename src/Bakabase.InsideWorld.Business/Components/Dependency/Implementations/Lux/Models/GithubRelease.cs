using System;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux.Models
{
    public class GithubRelease
    {
        public string url { get; set; }
        public int id { get; set; }
        public string node_id { get; set; }
        public string name { get; set; }
        public string label { get; set; }
        public Uploader uploader { get; set; }
        public string content_type { get; set; }
        public string state { get; set; }
        public int size { get; set; }
        public int download_count { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public string browser_download_url { get; set; }
    }
}
