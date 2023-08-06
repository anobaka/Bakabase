using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Http
{
    public class ThirdPartyHttpClientOptions
    {
        public int MaxThreads { get; set; } = 1;
        public int Interval { get; set; } = 0;
        public string Cookie { get; set; }
        public string UserAgent { get; set; } = DefaultUserAgent;
        public string Referer { get; set; }

        public Dictionary<string, string> Headers { get; set; }

        public const string DefaultUserAgent =
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/87.0.4280.141 Safari/537.36";
    }
}