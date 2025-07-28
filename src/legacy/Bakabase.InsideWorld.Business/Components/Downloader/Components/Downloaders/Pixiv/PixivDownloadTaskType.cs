using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Pixiv
{
    public enum PixivDownloadTaskType
    {
        [Downloader(ThirdPartyId.Pixiv,
            typeof(PixivSearchDownloader),
            $"{{{nameof(PixivNamingFields.UserName)}}}/[{{{nameof(PixivNamingFields.IllustrationId)}}}]{{{nameof(PixivNamingFields.IllustrationTitle)}}}_{{{nameof(PixivNamingFields.PageNo)}}}{{{nameof(PixivNamingFields.Extension)}}}",
            typeof(PixivNamingFields),
            typeof(PixivDownloaderHelper))]
        Search = 1,
        
        [Downloader(ThirdPartyId.Pixiv,
            typeof(PixivRankingDownloader),
            $"{{{nameof(PixivNamingFields.UserName)}}}/[{{{nameof(PixivNamingFields.IllustrationId)}}}]{{{nameof(PixivNamingFields.IllustrationTitle)}}}_{{{nameof(PixivNamingFields.PageNo)}}}{{{nameof(PixivNamingFields.Extension)}}}",
            typeof(PixivNamingFields),
            typeof(PixivDownloaderHelper))]
        Ranking = 2,
        
        [Downloader(ThirdPartyId.Pixiv,
            typeof(PixivFollowingDownloader),
            $"{{{nameof(PixivNamingFields.UserName)}}}/[{{{nameof(PixivNamingFields.IllustrationId)}}}]{{{nameof(PixivNamingFields.IllustrationTitle)}}}_{{{nameof(PixivNamingFields.PageNo)}}}{{{nameof(PixivNamingFields.Extension)}}}",
            typeof(PixivNamingFields),
            typeof(PixivDownloaderHelper))]
        /// <summary>
        /// For https://www.pixiv.net/ajax/follow_latest/illust?p=x&mode=all only
        /// </summary>
        Following = 3
    }
}
