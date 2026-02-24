using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    public enum ExHentaiDownloadTaskType
    {
        [Downloader(ThirdPartyId.ExHentai,
            typeof(ExHentaiSingleWorkDownloader),
            $"[{{{nameof(ExHentaiNamingFields.Category)}}}] {{{nameof(ExHentaiNamingFields.RawName)}}}/{{{nameof(ExHentaiNamingFields.PageTitle)}}}{{{nameof(ExHentaiNamingFields.Extension)}}}",
            typeof(ExHentaiNamingFields),
            typeof(ExHentaiDownloaderHelper))]
        SingleWork = 1,
        
        [Downloader(ThirdPartyId.ExHentai,
            typeof(ExHentaiWatchedDownloader),
            $"[{{{nameof(ExHentaiNamingFields.Category)}}}] {{{nameof(ExHentaiNamingFields.RawName)}}}/{{{nameof(ExHentaiNamingFields.PageTitle)}}}{{{nameof(ExHentaiNamingFields.Extension)}}}",
            typeof(ExHentaiNamingFields),
            typeof(ExHentaiDownloaderHelper))]
        Watched = 2,
        
        [Downloader(ThirdPartyId.ExHentai,
            typeof(ExHentaiListDownloader),
            $"[{{{nameof(ExHentaiNamingFields.Category)}}}] {{{nameof(ExHentaiNamingFields.RawName)}}}/{{{nameof(ExHentaiNamingFields.PageTitle)}}}{{{nameof(ExHentaiNamingFields.Extension)}}}",
            typeof(ExHentaiNamingFields),
            typeof(ExHentaiDownloaderHelper))]
        List = 3
    }
}
