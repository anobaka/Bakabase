using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    public record Favorites
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public int MediaCount { get; set; }
    }
}