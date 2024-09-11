using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using static Bakabase.Abstractions.Components.Configuration.InternalOptions;

namespace Bakabase.InsideWorld.Business.Models.Dto
{
    public record InternalOptionsDto
    {
        public ResourceOptions Resource { get; set; } = new();

        public record ResourceOptions
        {
            public IDictionary<int, Property> InternalResourcePropertyDescriptorMap { get; set; } =
                InternalOptions.InternalResourcePropertyDescriptorMap.ToDictionary(x => (int) x.Key, x => x.Value);

            public IDictionary<int, Property> ReservedResourcePropertyDescriptorMap { get; set; } =
                InternalOptions.ReservedResourcePropertyDescriptorMap.ToDictionary(x => (int) x.Key, x => x.Value);

            public Dictionary<int, SearchOperation[]> CustomPropertyValueSearchOperationsMap { get; set; } = new();
        }
    }
}