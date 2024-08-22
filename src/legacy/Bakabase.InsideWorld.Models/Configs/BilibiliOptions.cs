using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.InsideWorld.Models.Configs.Infrastructures;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs
{
    [Options(fileKey: "third-party-bilibili")]
    public class BilibiliOptions
    {
        public CommonDownloaderOptions? Downloader { get; set; }
        public string? Cookie { get; set; }
    }
}
