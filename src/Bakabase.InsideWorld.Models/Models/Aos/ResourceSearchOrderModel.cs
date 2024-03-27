using Bakabase.InsideWorld.Models.Constants.Aos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public record ResourceSearchOrderModel
    {
        public ResourceSearchSortableProperty Property { get; set; }
        public bool Asc { get; set; }
    }
}
