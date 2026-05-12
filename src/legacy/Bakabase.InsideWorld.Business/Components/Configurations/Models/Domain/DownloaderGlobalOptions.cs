using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain
{
    [Options(fileKey: "downloader")]
    public class DownloaderGlobalOptions
    {
        public bool AutoStartAfterCreation { get; set; }
    }
}
