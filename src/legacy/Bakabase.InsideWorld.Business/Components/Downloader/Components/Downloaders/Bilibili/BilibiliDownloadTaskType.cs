using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Bilibili
{
    public enum BilibiliDownloadTaskType
    {
        [Downloader(ThirdPartyId.Bilibili,
            typeof(BilibiliDownloader),
            $"{{{nameof(BilibiliNamingFields.UploaderName)}}}/[{{{nameof(BilibiliNamingFields.BvId)}}}]{{{nameof(BilibiliNamingFields.PostTitle)}}}/{{{nameof(BilibiliNamingFields.PartNo)}}}.{{{nameof(BilibiliNamingFields.PartName)}}}.{{{nameof(BilibiliNamingFields.QualityName)}}}{{{nameof(BilibiliNamingFields.Extension)}}}",
            typeof(BilibiliNamingFields),
            typeof(BilibiliDownloaderHelper))]
        Favorites = 1
    }
}