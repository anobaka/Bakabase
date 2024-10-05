﻿using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Configurations.Models.Domain;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Service.Models.Input
{
    public class ResourceOptionsPatchInputModel
    {
        public AdditionalCoverDiscoveringSource[]? AdditionalCoverDiscoveringSources { get; set; }
        public ResourceOptions.CoverOptionsModel? CoverOptions { get; set; }
        public PropertyValueScope[]? PropertyValueScopePriority { get; set; }
        public ResourceSearchInputModel? SearchCriteria { get; set; }
    }
}
