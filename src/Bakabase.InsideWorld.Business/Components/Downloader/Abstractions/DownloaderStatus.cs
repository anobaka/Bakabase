using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions
{
    public enum DownloaderStatus
    {
        JustCreated = 0,
        Starting = 100,
        Downloading = 200,
        Complete = 300,
        Failed = 400,
        Stopping = 500,
        Stopped = 600
    }
}
