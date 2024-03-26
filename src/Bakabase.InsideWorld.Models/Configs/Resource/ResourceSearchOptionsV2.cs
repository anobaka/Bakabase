using Bakabase.InsideWorld.Models.Models.Aos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Configs.Resource
{
    public record ResourceSearchOptionsV2
    {
        public ResourceSearchFilterGroup? Group { get; set; }
        public ResourceSearchOrderModel[]? Orders { get; set; }
    }
}
