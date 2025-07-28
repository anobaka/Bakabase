using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Patreon
{
    /// <summary>
    /// Patreon download task types
    /// </summary>
    public enum PatreonDownloadTaskType
    {
        /// <summary>
        /// Download all posts from a specific creator
        /// </summary>
        [Downloader(ThirdPartyId.Patreon,
            typeof(PatreonCreatorDownloader),
            $"{{{nameof(PatreonNamingFields.CreatorName)}}}/[{{{nameof(PatreonNamingFields.PostId)}}}]{{{nameof(PatreonNamingFields.PostTitle)}}}_{{{nameof(PatreonNamingFields.FileNo)}}}{{{nameof(PatreonNamingFields.Extension)}}}",
            typeof(PatreonNamingFields),
            typeof(PatreonDownloaderHelper))]
        Creator = 1,
        
        /// <summary>
        /// Download posts from following creators
        /// </summary>
        [Downloader(ThirdPartyId.Patreon,
            typeof(PatreonFollowingDownloader),
            $"{{{nameof(PatreonNamingFields.CreatorName)}}}/[{{{nameof(PatreonNamingFields.PostId)}}}]{{{nameof(PatreonNamingFields.PostTitle)}}}_{{{nameof(PatreonNamingFields.FileNo)}}}{{{nameof(PatreonNamingFields.Extension)}}}",
            typeof(PatreonNamingFields),
            typeof(PatreonDownloaderHelper))]
        Following = 2,
        
        /// <summary>
        /// Download a single post
        /// </summary>
        [Downloader(ThirdPartyId.Patreon,
            typeof(PatreonSinglePostDownloader),
            $"{{{nameof(PatreonNamingFields.CreatorName)}}}/[{{{nameof(PatreonNamingFields.PostId)}}}]{{{nameof(PatreonNamingFields.PostTitle)}}}_{{{nameof(PatreonNamingFields.FileNo)}}}{{{nameof(PatreonNamingFields.Extension)}}}",
            typeof(PatreonNamingFields),
            typeof(PatreonDownloaderHelper))]
        SinglePost = 3
    }
}