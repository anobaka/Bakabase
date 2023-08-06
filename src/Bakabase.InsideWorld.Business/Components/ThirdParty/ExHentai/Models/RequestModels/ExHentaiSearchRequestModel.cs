using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai.Models.Constants;
using Bootstrap.Models.RequestModels;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai.Models.RequestModels
{
    public class ExHentaiSearchRequestModel : SearchRequestModel
    {
        public string Keyword { get; set; }
        public List<ExHentaiCategory> HideCategories { get; set; } = new();
    }
}