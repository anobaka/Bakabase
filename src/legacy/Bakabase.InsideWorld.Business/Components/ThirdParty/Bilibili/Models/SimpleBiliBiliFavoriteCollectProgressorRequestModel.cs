namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models
{
    public class SimpleBiliBiliFavoriteCollectProgressorRequestModel
    {
        public string RootPath { get; set; }
        public int[] FavoritesIds { get; set; }
        public string Cookie { get; set; }
        public string AnniePath { get; set; }
        public int FavoritesToArchiveId { get; set; }
    }
}
