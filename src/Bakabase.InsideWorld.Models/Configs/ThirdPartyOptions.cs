using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs
{
    [Options]
    public class ThirdPartyOptions
    {
        public List<SimpleSearchEngineOptions>? SimpleSearchEngines { get; set; }
        public FFmpegOptions? FFmpeg { get; set; }

        public class SimpleSearchEngineOptions
        {
            public string Name { get; set; } = string.Empty;
            public string UrlTemplate { get; set; } = string.Empty;
        }

        public class FFmpegOptions
        {
            public string BinDirectory { get; set; } = string.Empty;
        }
    }
}