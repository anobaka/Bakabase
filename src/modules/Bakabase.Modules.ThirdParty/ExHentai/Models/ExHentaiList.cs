namespace Bakabase.Modules.ThirdParty.ExHentai.Models
{
    public class ExHentaiList
    {
        public int ResultCount { get; set; }
        public string NextListUrl { get; set; }
        public List<ExHentaiResource> Resources { get; set; }
    }
}
