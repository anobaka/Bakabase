using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Configs.Resource
{
    [Obsolete]
    public class ResourceSearchSlotItemOptions
    {
        public string Name { get; set; }
        public ResourceSearchOptions Model { get; set; }
    }
}