namespace Bakabase.Modules.ThirdParty.ThirdParties.Pixiv.Models
{
    public class PixivRankingItem
    {
        public string Title { get; set; }
        public string Date { get; set; }
        public string[] Tags { get; set; }
        public string Url { get; set; }
        public string Illust_type { get; set; }
        public string Illust_book_style { get; set; }
        public int Illust_page_count { get; set; }
        public string User_name { get; set; }
        public string Profile_img { get; set; }
        public ContentType Illust_content_type { get; set; }
        //public bool Illust_series { get; set; }
        public string Illust_id { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string User_id { get; set; }
        public int Rank { get; set; }
        public int Yes_rank { get; set; }
        public int Rating_count { get; set; }
        public int View_count { get; set; }
        public long Illust_upload_timestamp { get; set; }
        public string Attr { get; set; }
        public bool Is_bookmarked { get; set; }
        public bool Bookmarkable { get; set; }

        public class ContentType
        {
            public int Sexual { get; set; }
            public bool Lo { get; set; }
            public bool Grotesque { get; set; }
            public bool Violent { get; set; }
            public bool Homosexual { get; set; }
            public bool Drug { get; set; }
            public bool Thoughts { get; set; }
            public bool Antisocial { get; set; }
            public bool Religion { get; set; }
            public bool Original { get; set; }
            public bool Furry { get; set; }
            public bool Bl { get; set; }
            public bool Yuri { get; set; }
        }
    }
}