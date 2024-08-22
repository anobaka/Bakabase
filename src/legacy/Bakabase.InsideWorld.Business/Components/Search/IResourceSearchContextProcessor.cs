using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.InsideWorld.Business.Components.Search
{
	public interface IResourceSearchContextProcessor
	{
		/// <summary>
		/// 
		/// </summary>
		/// <param name="filter"></param>
		/// <param name="context"></param>
		Task<HashSet<int>?> Search(ResourceSearchFilter filter, ResourceSearchContext context);
	}
}
