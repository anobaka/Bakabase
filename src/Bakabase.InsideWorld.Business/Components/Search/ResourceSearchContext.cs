﻿using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Search
{
	public record ResourceSearchContext
	{
		public HashSet<int> AllResourceIds { get; }
		public HashSet<int> ResourceIdCandidates { get; }

		public ResourceSearchContext(IEnumerable<Models.Models.Entities.Resource> allResources)
		{
			ResourcesPool = allResources.ToDictionary(x => x.Id, x => x);
			ResourceIdCandidates = ResourcesPool.Keys.ToHashSet();
			AllResourceIds = new HashSet<int>(ResourceIdCandidates);
		}

		public Dictionary<string, HashSet<string>>? Aliases;

		/// <summary>
		/// It may take more time to group by value than raw list.
		/// todo: it takes 3x time using "object as" in enumerating than typed values
		/// </summary>
		public Dictionary<int, Dictionary<int, object?>?>? CustomPropertyDataPool;

		public Dictionary<int, Models.Models.Entities.Resource>? ResourcesPool { get; }

		/// <summary>
		/// FavoritesId - ResourceIds
		/// </summary>
		public Dictionary<int, HashSet<int>>? FavoritesResourceDataPool { get; set; }
		/// <summary>
		/// TagId - ResourceIds
		/// </summary>
		public Dictionary<int, HashSet<int>>? TagResourceDataPool { get; set; }

		public Dictionary<int, CustomPropertyDto>? PropertiesDataPool { get; set; }
	}
}