using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input
{
    /// <summary>
    /// Batch-query whether a set of third-party items has been downloaded before.
    /// </summary>
    public class DownloadRecordQueryInputModel
    {
        public ThirdPartyId ThirdPartyId { get; set; }

        /// <summary>
        /// Third-party item keys to look up (same value as the download task Key).
        /// </summary>
        public List<string> Keys { get; set; } = [];
    }
}
