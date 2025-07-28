using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Fantia
{
    /// <summary>
    /// Fantia download task types
    /// </summary>
    public enum FantiaDownloadTaskType
    {
        /// <summary>
        /// Download all posts from a specific creator
        /// </summary>
        [Downloader(ThirdPartyId.Fantia,
            typeof(FantiaCreatorDownloader),
            $"{{{nameof(FantiaNamingFields.FanclubName)}}}/[{{{nameof(FantiaNamingFields.PostId)}}}]{{{nameof(FantiaNamingFields.PostTitle)}}}/{{{nameof(FantiaNamingFields.FileNo)}}}{{{nameof(FantiaNamingFields.Extension)}}}",
            typeof(FantiaNamingFields),
            typeof(FantiaDownloaderHelper))]
        Creator = 1,
        
        /// <summary>
        /// Download posts from following creators
        /// </summary>
        [Downloader(ThirdPartyId.Fantia,
            typeof(FantiaFollowingDownloader),
            $"{{{nameof(FantiaNamingFields.FanclubName)}}}/[{{{nameof(FantiaNamingFields.PostId)}}}]{{{nameof(FantiaNamingFields.PostTitle)}}}/{{{nameof(FantiaNamingFields.FileNo)}}}{{{nameof(FantiaNamingFields.Extension)}}}",
            typeof(FantiaNamingFields),
            typeof(FantiaDownloaderHelper))]
        Following = 2,
        
        /// <summary>
        /// Download a single post
        /// </summary>
        [Downloader(ThirdPartyId.Fantia,
            typeof(FantiaSinglePostDownloader),
            $"{{{nameof(FantiaNamingFields.FanclubName)}}}/[{{{nameof(FantiaNamingFields.PostId)}}}]{{{nameof(FantiaNamingFields.PostTitle)}}}/{{{nameof(FantiaNamingFields.FileNo)}}}{{{nameof(FantiaNamingFields.Extension)}}}",
            typeof(FantiaNamingFields),
            typeof(FantiaDownloaderHelper))]
        SinglePost = 3
    }
}