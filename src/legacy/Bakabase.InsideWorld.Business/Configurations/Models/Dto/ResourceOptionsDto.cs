using System;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.InsideWorld.Business.Configurations.Models.Domain;
using Bakabase.InsideWorld.Models.Constants;
using static Bakabase.InsideWorld.Business.Configurations.Models.Domain.ResourceOptions;

namespace Bakabase.InsideWorld.Business.Configurations.Models.Dto
{
    public record ResourceOptionsDto
    {
        public DateTime LastSyncDt { get; set; }
        public DateTime LastNfoGenerationDt { get; set; }
        public ResourceSearchDto? LastSearchV2 { get; set; }
        public ResourceOptions.CoverOptionsModel CoverOptions { get; set; } = new();
        public AdditionalCoverDiscoveringSource[] AdditionalCoverDiscoveringSources { get; set; } = Array.Empty<AdditionalCoverDiscoveringSource>();
        public bool HideChildren { get; set; }
        public PropertyValueScope[] PropertyValueScopePriority { get; set; } = [];
    }
}
