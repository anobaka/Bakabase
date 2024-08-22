using Bakabase.InsideWorld.Models.Configs.Infrastructures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs
{
    [Options(fileKey: "third-party-exhentai")]
    public class ExHentaiOptions
    {
        public CommonDownloaderOptions? Downloader { get; set; }
        public string? Cookie { get; set; }
        public ExHentaiEnhancerOptions? Enhancer { get; set; }

        public class ExHentaiEnhancerOptions
        {
            public string[] ExcludedTags { get; set; } = Array.Empty<string>();
        }
    }
}