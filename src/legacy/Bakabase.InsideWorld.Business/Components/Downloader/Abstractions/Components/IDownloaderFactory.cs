using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components
{
    /// <summary>
    /// Registry service for downloader definitions and metadata
    /// </summary>
    public interface IDownloaderFactory
    {
        List<DownloaderDefinition> GetDefinitions();

        IDownloaderHelper GetHelper(ThirdPartyId thirdPartyId, int taskType);

        IDownloader GetDownloader(ThirdPartyId thirdPartyId, int taskType);
    }
}