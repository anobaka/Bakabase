namespace Bakabase.Modules.ThirdParty.ThirdParties.Pixiv.Models
{
    public class PixivIllustration
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
        public IllustrationUrls Urls { get; set; }
        public string Alt { get; set; }
        public TagsModel Tags { get; set; }
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
        public IllustrationFanboxPromotionModel FanboxPromotion { get; set; }
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
        public IllustrationTitleCaptionTranslation TitleCaptionTranslation { get; set; }
        public DateTime UpdateDate { get; set; }
        public bool IsMasked { get; set; }

        public class IllustrationTitleCaptionTranslation
        {
            public string WorkTitle { get; set; }
            public string WorkCaption { get; set; }
        }

        public class IllustrationUrls
        {
            public string Mini { get; set; }
            public string Thumb { get; set; }
            public string Small { get; set; }
            public string Regular { get; set; }
            public string Original { get; set; }
        }

        public class IllustrationFanboxPromotionModel
        {
            public string UserName { get; set; }
            public string UserImageUrl { get; set; }
            public string ContentUrl { get; set; }
            public string Description { get; set; }
            public string ImageUrl { get; set; }
            public string ImageUrlMobile { get; set; }
            public bool HasAdultContent { get; set; }
        }

        public class TagsModel
        {
            public string AuthorId { get; set; }
            public bool IsLocked { get; set; }
            public bool Writable { get; set; }
            public ImageTag[] Tags { get; set; }

            public class ImageTag
            {
                public string Tag { get; set; }
                public bool Locked { get; set; }
                public bool Deletable { get; set; }
                public string UserId { get; set; }
                public string UserName { get; set; }
            }

        }
    }
}