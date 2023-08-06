using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.JavLibrary
{
    public class JavLibraryDownloaderStartRequestModel
    {
        public string DownloadPath { get; set; }
        public string Cookie { get; set; }
        public string[] Urls { get; set; }
        public string[] TorrentLinkKeywords { get; set; } = new[] { "dacload.com", "finedac.com", "dacdate.com" };
    }
}