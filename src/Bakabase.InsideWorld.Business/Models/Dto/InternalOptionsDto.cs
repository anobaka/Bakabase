using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Models.Dto
{
	public record InternalOptionsDto
	{
		public ResourceOptions Resource { get; set; } = new();

		public record ResourceOptions
		{
			public IDictionary<int, CustomPropertyType> ReservedResourcePropertyAndValueTypeMap { get; set; } =
				InternalOptions.ReservedResourcePropertyAndValueTypeMap.ToDictionary(x => (int) x.Key, x => x.Value);

            public Dictionary<int, SearchOperation[]> StandardValueSearchOperationsMap { get; set; } = new();
        }
	}
}