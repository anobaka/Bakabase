using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input
{
    public record DownloadTaskStartRequestModel
    {
        public int[] Ids { get; set; } = [];
        public DownloadTaskActionOnConflict ActionOnConflict { get; set; }
    }
}
