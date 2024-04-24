using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Configs.Fixed
{
	public record ReservedOptions
	{
		public ResourceOptions Resource { get; set; } = new();

		public record ResourceOptions
		{
			// public IDictionary<int, int> ReservedResourcePropertyAndValueTypeMap { get; set; } =
			// 	BusinessConstants.ReservedResourcePropertyAndValueTypeMap.ToDictionary(x => (int) x.Key, x => x.Value);

            public Dictionary<int, SearchOperation[]> StandardValueSearchOperationsMap { get; set; } = new();
        }
	}
}