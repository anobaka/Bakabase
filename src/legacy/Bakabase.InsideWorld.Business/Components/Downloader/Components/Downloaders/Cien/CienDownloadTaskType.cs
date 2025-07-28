using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Cien
{
    /// <summary>
    /// Cien download task types
    /// </summary>
    public enum CienDownloadTaskType
    {
        /// <summary>
        /// Download all posts from a specific creator
        /// </summary>
        [Downloader(ThirdPartyId.Cien,
            typeof(CienCreatorDownloader),
            $"{{{nameof(CienNamingFields.AuthorName)}}}/[{{{nameof(CienNamingFields.ArticleId)}}}]{{{nameof(CienNamingFields.ArticleTitle)}}}/{{{nameof(CienNamingFields.FileNo)}}}{{{nameof(CienNamingFields.Extension)}}}",
            typeof(CienNamingFields),
            typeof(CienDownloaderHelper))]
        Creator = 1,

        /// <summary>
        /// Download posts from following creators
        /// </summary>
        [Downloader(ThirdPartyId.Cien,
            typeof(CienFollowingDownloader),
            $"{{{nameof(CienNamingFields.AuthorName)}}}/[{{{nameof(CienNamingFields.ArticleId)}}}]{{{nameof(CienNamingFields.ArticleTitle)}}}/{{{nameof(CienNamingFields.FileNo)}}}{{{nameof(CienNamingFields.Extension)}}}",
            typeof(CienNamingFields),
            typeof(CienDownloaderHelper))]
        Following = 2,

        /// <summary>
        /// Download a single post
        /// </summary>
        [Downloader(ThirdPartyId.Cien,
            typeof(CienSinglePostDownloader),
            $"{{{nameof(CienNamingFields.AuthorName)}}}/[{{{nameof(CienNamingFields.ArticleId)}}}]{{{nameof(CienNamingFields.ArticleTitle)}}}/{{{nameof(CienNamingFields.FileNo)}}}{{{nameof(CienNamingFields.Extension)}}}",
            typeof(CienNamingFields),
            typeof(CienDownloaderHelper))]
        SinglePost = 3
    }
}