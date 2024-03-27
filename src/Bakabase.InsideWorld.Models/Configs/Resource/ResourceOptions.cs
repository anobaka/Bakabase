using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Configuration.Abstractions;
using Newtonsoft.Json;
using static Bakabase.InsideWorld.Models.Configs.FileSystemOptions;

namespace Bakabase.InsideWorld.Models.Configs.Resource
{
    [Options]
    public record ResourceOptions
    {
        public DateTime LastSyncDt { get; set; }
        public DateTime LastNfoGenerationDt { get; set; }
        public ResourceSearchOptionsV2? LastSearchV2 { get; set; }
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