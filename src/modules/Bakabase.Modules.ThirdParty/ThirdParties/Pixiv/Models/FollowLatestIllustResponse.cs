namespace Bakabase.Modules.ThirdParty.ThirdParties.Pixiv.Models
{
    public class FollowLatestIllustResponse
    {
        public PageModel Page { get; set; }
        public Dictionary<string, Dictionary<string, string>> TagTranslation { get; set; }
        public ThumbnailsModel Thumbnails { get; set; }

        public class PageModel
        {
            public List<string> Ids { get; set; }
            public bool IsLastPage { get; set; }
            public List<string> Tags { get; set; }
        }

        public class ThumbnailsModel
        {
            public List<FollowIllust> Illust { get; set; }

            public class FollowIllust
            {
                public string IllustId { get; set; }
                public string IllustTitle { get; set; }
                public string Id { get; set; }
                public string Title { get; set; }
                public int IllustType { get; set; }
                public DateTime CreateDate { get; set; }
                public DateTime UploadDate { get; set; }
                public int Restrict { get; set; }
                public int XRestrict { get; set; }
                public int Sl { get; set; }
                public PixivIllustration.IllustrationUrls Urls { get; set; }
                public string Alt { get; set; }
                public string[] Tags { get; set; }
                public string UserId { get; set; }
                public string UserName { get; set; }
                public string UserAccount { get; set; }
                public bool LikeData { get; set; }
                public int Width { get; set; }
                public int Height { get; set; }
                public int PageCount { get; set; }
                public int BookmarkCount { get; set; }
                public int LikeCount { get; set; }
                public int CommentCount { get; set; }
                public int ResponseCount { get; set; }
                public int ViewCount { get; set; }
                public int BookStyle { get; set; }
                public bool IsHowto { get; set; }
                public bool IsOriginal { get; set; }
                public int ImageResponseCount { get; set; }
                public bool IsBookmarkable { get; set; }
                public bool IsUnlisted { get; set; }
                public int CommentOff { get; set; }
                public int AiType { get; set; }
                public string Url { get; set; }
                public string ProfileImageUrl { get; set; }
                public int TextCount { get; set; }
                public int WordCount { get; set; }
                public int ReadingTime { get; set; }
                public bool UseWordCount { get; set; }
                public string Description { get; set; }
                public string Marker { get; set; }
                public DateTime UpdateDate { get; set; }
                public bool IsMasked { get; set; }
            }
        }
    }
}
