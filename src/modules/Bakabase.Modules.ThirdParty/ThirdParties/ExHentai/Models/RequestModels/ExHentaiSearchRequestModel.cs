using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models.Constants;
using Bootstrap.Models.RequestModels;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models.RequestModels
{
    public record ExHentaiSearchRequestModel : SearchRequestModel
    {
        public string Keyword { get; set; }
        public List<ExHentaiCategory> HideCategories { get; set; } = new();
    }
}