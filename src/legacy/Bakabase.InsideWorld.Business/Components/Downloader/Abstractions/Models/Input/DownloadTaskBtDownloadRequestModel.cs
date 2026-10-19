using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;

public record DownloadTaskBtDownloadRequestModel
{
    public DownloadTaskActionOnConflict ActionOnConflict { get; set; }
}
