using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services
{
	public class CategoryCustomPropertyMappingService: FullMemoryCacheResourceService<
		InsideWorldDbContext, CategoryCustomPropertyMapping, int>
	{
		public CategoryCustomPropertyMappingService(IServiceProvider serviceProvider) : base(serviceProvider)
		{
		}
	}
}