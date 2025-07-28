using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Fanbox
{
    /// <summary>
    /// Fanbox download task types
    /// </summary>
    public enum FanboxDownloadTaskType
    {
        /// <summary>
        /// Download all posts from a specific creator
        /// </summary>
        [Downloader(ThirdPartyId.Fanbox,
            typeof(FanboxCreatorDownloader),
            $"{{{nameof(FanboxNamingFields.CreatorName)}}}/[{{{nameof(FanboxNamingFields.PostId)}}}]{{{nameof(FanboxNamingFields.PostTitle)}}}/{{{nameof(FanboxNamingFields.FileNo)}}}{{{nameof(FanboxNamingFields.Extension)}}}",
            typeof(FanboxNamingFields), typeof(FanboxDownloaderHelper))]
        Creator = 1,
        
        /// <summary>
        /// Download posts from following creators
        /// </summary>
        [Downloader(ThirdPartyId.Fanbox,
            typeof(FanboxFollowingDownloader),
            $"{{{nameof(FanboxNamingFields.CreatorName)}}}/[{{{nameof(FanboxNamingFields.PostId)}}}]{{{nameof(FanboxNamingFields.PostTitle)}}}/{{{nameof(FanboxNamingFields.FileNo)}}}{{{nameof(FanboxNamingFields.Extension)}}}",
            typeof(FanboxNamingFields),  typeof(FanboxDownloaderHelper))]
        Following = 2,
        
        /// <summary>
        /// Download a single post
        /// </summary>
        [Downloader(ThirdPartyId.Fanbox,
            typeof(FanboxSinglePostDownloader),
            $"{{{nameof(FanboxNamingFields.CreatorName)}}}/[{{{nameof(FanboxNamingFields.PostId)}}}]{{{nameof(FanboxNamingFields.PostTitle)}}}/{{{nameof(FanboxNamingFields.FileNo)}}}{{{nameof(FanboxNamingFields.Extension)}}}",
            typeof(FanboxNamingFields),  typeof(FanboxDownloaderHelper))]
        SinglePost = 3
    }
}