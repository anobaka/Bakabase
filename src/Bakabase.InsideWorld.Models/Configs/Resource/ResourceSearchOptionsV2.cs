using Bakabase.InsideWorld.Models.Models.Aos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Configs.Resource
{
    public class ResourceSearchOptionsV2: SearchRequestModel
    {
        public ResourceSearchFilterGroup? Group { get; set; }
        public ResourceSearchOrderModel[]? Orders { get; set; }
    }
}
