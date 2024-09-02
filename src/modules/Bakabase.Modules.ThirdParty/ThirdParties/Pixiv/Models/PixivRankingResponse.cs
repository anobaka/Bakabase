namespace Bakabase.Modules.ThirdParty.ThirdParties.Pixiv.Models
{
    public class PixivRankingResponse
    {
        public PixivRankingItem[] Contents { get; set; }
        public string Mode { get; set; }
        public string Content { get; set; }
        public int Page { get; set; }
        /// <summary>
        /// false, 1, 2, 3
        /// </summary>
        public string Prev { get; set; }
        /// <summary>
        /// false, 1, 2, 3
        /// </summary>
        public string Next { get; set; }
        public string Date { get; set; }
        public string Prev_date { get; set; }
        public string Next_date { get; set; }
        public int Rank_total { get; set; }
    }
}
