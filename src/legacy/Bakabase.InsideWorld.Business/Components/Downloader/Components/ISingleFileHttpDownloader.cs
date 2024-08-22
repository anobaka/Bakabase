using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components
{
    internal interface ISingleFileHttpDownloader
    {
        Task Download(string url, string filePath, CancellationToken ct);
    }
}