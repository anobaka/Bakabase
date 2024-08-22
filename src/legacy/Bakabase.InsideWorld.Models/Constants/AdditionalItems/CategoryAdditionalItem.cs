using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Constants.AdditionalItems
{
    [Flags]
    public enum CategoryAdditionalItem
    {
        None = 0,
        Components = 1 << 0,
        Validation = 1 << 1 | Components,
        CustomProperties = 1 << 2,
        EnhancerOptions = 1 << 3
    }
}