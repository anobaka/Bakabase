using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services
{
	public class CustomPropertyService : FullMemoryCacheResourceService<InsideWorldDbContext, CustomProperty, int>
	{
		public CustomPropertyService(IServiceProvider serviceProvider) : base(serviceProvider)
		{
		}

		public async Task<List<CustomPropertyDto>> GetDtoList(Expression<Func<CustomProperty, bool>> selector = null, bool returnCopy = true)
		{
			var data = await GetAll(selector, returnCopy);

			return data.Select(x => x.ToDto()!).ToList();
		}
	}
}
