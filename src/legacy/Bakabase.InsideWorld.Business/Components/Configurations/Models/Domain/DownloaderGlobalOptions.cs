using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain
{
    [Options(fileKey: "downloader")]
    public class DownloaderGlobalOptions
    {
        public bool AutoStartAfterCreation { get; set; }

        /// <summary>Root directory for sources that have no download directory of their own.</summary>
        public string? DefaultDownloadPath { get; set; }
    }
}
