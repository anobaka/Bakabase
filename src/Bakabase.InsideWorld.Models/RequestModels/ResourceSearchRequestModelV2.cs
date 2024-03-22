using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Configs.Resource;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bootstrap.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.RequestModels
{
	public class ResourceSearchRequestModelV2: SearchRequestModel
	{
		public ResourceSearchFilterGroup? Group { get; set; }
		public ResourceSearchOptions.OrderModel[]? Orders { get; set; }
	}
}
