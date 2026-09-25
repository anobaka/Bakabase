namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    /// <summary>One entry of <c>view.pages[]</c> and of <c>x/player/pagelist</c> <c>data[]</c>.</summary>
    public class PostPage
    {
        public long Cid { get; set; }
        public int Page { get; set; }
        public Dimension? Dimension { get; set; }
        public string? Part { get; set; }

        /// <summary>Seconds.</summary>
        public int Duration { get; set; }
    }
}
