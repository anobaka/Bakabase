using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs
{
    [Options(fileKey: "third-party-jav-library")]
    public class JavLibraryOptions
    {
        public string? Cookie { get; set; }
        public CollectorOptions? Collector { get; set; }

        public class CollectorOptions
        {
            public string? Path { get; set; }
            public HashSet<string>? Urls { get; set; } 
            public HashSet<string>? TorrentOrLinkKeywords { get; set; }
        }
    }
}
