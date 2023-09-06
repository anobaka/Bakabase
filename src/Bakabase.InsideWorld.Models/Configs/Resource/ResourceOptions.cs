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
    public class ResourceOptions
    {
        public DateTime LastSyncDt { get; set; }
        public DateTime LastNfoGenerationDt { get; set; }
        public ResourceSearchOptions? LastSearch { get; set; }
        public List<ResourceSearchSlotItemOptions> SearchSlots { get; set; } = new();
        public CoverOptionsModel CoverOptions { get; set; } = new();


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
                LastSearch = options.LastSearch?.ToDto(),
                LastSyncDt = options.LastSyncDt,
                SearchSlots = options.SearchSlots?.Select(a => a.ToDto()).ToList() ??
                              new List<ResourceSearchSlotItemDto>(),
                CoverOptions = options.CoverOptions
            };
        }
    }
}