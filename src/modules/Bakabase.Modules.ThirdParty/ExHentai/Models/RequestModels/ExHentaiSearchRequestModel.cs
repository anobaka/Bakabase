﻿using Bakabase.Modules.ThirdParty.ExHentai.Models.Constants;
using Bootstrap.Models.RequestModels;

namespace Bakabase.Modules.ThirdParty.ExHentai.Models.RequestModels
{
    public class ExHentaiSearchRequestModel : SearchRequestModel
    {
        public string Keyword { get; set; }
        public List<ExHentaiCategory> HideCategories { get; set; } = new();
    }
}