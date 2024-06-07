using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using CustomProperty = Bakabase.Modules.CustomProperty.Models.CustomProperty;

namespace Bakabase.InsideWorld.Business.Components.Search
{
	public record ResourceSearchContext
	{
		public HashSet<int> AllResourceIds { get; }
		public HashSet<int> ResourceIdCandidates { get; }

		public ResourceSearchContext(IEnumerable<Abstractions.Models.Domain.Resource> allResources)
		{
			ResourcesPool = allResources.ToDictionary(x => x.Id, x => x);
			ResourceIdCandidates = ResourcesPool.Keys.ToHashSet();
			AllResourceIds = new HashSet<int>(ResourceIdCandidates);
		}

		/// <summary>
		/// Text - Candidates
		/// </summary>
		public Dictionary<string, HashSet<string>>? AliasCandidates;

		public Dictionary<int, Dictionary<int, CustomPropertyValue?>?>? CustomPropertyDataPool;

		public Dictionary<int, Abstractions.Models.Domain.Resource>? ResourcesPool { get; }

		/// <summary>
		/// FavoritesId - ResourceIds
		/// </summary>
		public Dictionary<int, HashSet<int>>? FavoritesResourceDataPool { get; set; }
		/// <summary>
		/// TagId - ResourceIds
		/// </summary>
		public Dictionary<int, HashSet<int>>? TagResourceDataPool { get; set; }

		public Dictionary<int, CustomProperty>? PropertiesDataPool { get; set; }
	}
}