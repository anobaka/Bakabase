using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

namespace Bakabase.Service.Models.Input;

public class ExHentaiDownloadTaskAddInputModel
{
    public ExHentaiDownloadTaskType Type { get; set; }
    public string Link { get; set; } = null!;
}