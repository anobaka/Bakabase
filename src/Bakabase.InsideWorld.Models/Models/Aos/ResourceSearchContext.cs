using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
	public record ResourceSearchContext
	{
		/// <summary>
		/// <para>Null: Valid for all</para>
		/// <para>Empty: Valid for no one</para>
		/// <para>Any: Valid for list</para>
		/// </summary>
		public HashSet<int>? LimitedResourceIds { get; private set; }

		public void UnionLimitedResourceIds(HashSet<int> resourceIds)
		{
			if (LimitedResourceIds == null)
			{
				LimitedResourceIds = resourceIds;
			}
			else
			{
				LimitedResourceIds.UnionWith(resourceIds);
			}
		}

		public void IntersectLimitedResourceIds(HashSet<int> resourceIds)
		{
			if (LimitedResourceIds == null)
			{
				LimitedResourceIds = resourceIds;
			}
			else
			{
				LimitedResourceIds.IntersectWith(resourceIds);
			}
		}

		/// <summary>
		/// <para>Empty: nothing invalid</para>
		/// <para>Any: Invalid for list</para>
		/// </summary>
		public HashSet<int> ExcludedResourceIds { get; set; } = [];

		public void UnionExcludedResourceIds(HashSet<int> resourceIds)
		{
			ExcludedResourceIds.UnionWith(resourceIds);
			LimitedResourceIds?.ExceptWith(resourceIds);
		}

		public void IntersectExcludedResourceIds(HashSet<int> resourceIds)
		{
			ExcludedResourceIds.IntersectWith(resourceIds);
			LimitedResourceIds?.ExceptWith(resourceIds);
		}


		public Dictionary<string, HashSet<string>>? Aliases;

		/// <summary>
		/// It may take more time to group by value than raw list.
		/// todo: it takes 3x time using "object as" in enumerating than typed values
		/// </summary>
		public Dictionary<int, List<(int ResourceId, object? Value)>>? CustomPropertyDataPool;

		public Dictionary<int, Resource>? ResourcesPool;

		public Dictionary<int, HashSet<int>>? FavoritesResourceDataPool { get; set; }
		public Dictionary<int, HashSet<int>>? TagResourceDataPool { get; set; }
	}
}