using System;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.InsideWorld.Business.Configurations.Models.Dto;
using Bakabase.InsideWorld.Business.Configurations.Models.Input;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Configurations.Models.Db
{
    [Options]
    public record ResourceOptions
    {
        public DateTime LastSyncDt { get; set; }
        public DateTime LastNfoGenerationDt { get; set; }
        public ResourceSearchDto? LastSearchV2 { get; set; }
        public CoverOptionsModel CoverOptions { get; set; } = new();
        public bool HideChildren { get; set; }

        public AdditionalCoverDiscoveringSource[] AdditionalCoverDiscoveringSources { get; set; } =
            Array.Empty<AdditionalCoverDiscoveringSource>();

        public record CoverOptionsModel
        {
            public CoverSaveLocation? SaveLocation { get; set; }
            public bool? Overwrite { get; set; }
        }
    }

    public static class ResourceOptionsExtensions
    {
        public static ResourceOptionsDto? ToDto(this ResourceOptions? options)
        {
            if (options == null)
            {
                return null;
            }

            return new ResourceOptionsDto
            {
                AdditionalCoverDiscoveringSources = options.AdditionalCoverDiscoveringSources,
                LastNfoGenerationDt = options.LastNfoGenerationDt,
                LastSearchV2 = options.LastSearchV2,
                LastSyncDt = options.LastSyncDt,
                CoverOptions = options.CoverOptions
            };
        }
    }
}