using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Configs.Infrastructures
{
    public class CommonDownloaderOptions
    {
        public const int DefaultThreads = 1;
        public const int DefaultInterval = 0;

        public int Threads { get; set; } = DefaultThreads;
        public int Interval { get; set; } = DefaultInterval;
        public string? DefaultPath { get; set; }
        public string? NamingConvention { get; set; }
    }
}
