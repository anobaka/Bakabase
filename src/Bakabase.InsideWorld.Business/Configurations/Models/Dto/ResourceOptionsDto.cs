using System;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.InsideWorld.Business.Configurations.Models.Db;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Configurations.Models.Input
{
    public record ResourceOptionsDto
    {
        public DateTime LastSyncDt { get; set; }
        public DateTime LastNfoGenerationDt { get; set; }
        public ResourceSearchDto? LastSearchV2 { get; set; }
        public ResourceOptions.CoverOptionsModel CoverOptions { get; set; } = new();
        public AdditionalCoverDiscoveringSource[] AdditionalCoverDiscoveringSources { get; set; } = Array.Empty<AdditionalCoverDiscoveringSource>();
    }
}
