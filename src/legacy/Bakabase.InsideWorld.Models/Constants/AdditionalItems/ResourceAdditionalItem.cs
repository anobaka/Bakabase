using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Constants.AdditionalItems
{
    [Flags]
    public enum ResourceAdditionalItem
    {
        None = 0,
        CustomProperties = 1 << 5 | Category,
        Alias = 1 << 6,
        Category = 1 << 7,
        DisplayName = 1 << 8 | CustomProperties | Category,
        HasChildren = 1 << 9,
        ReservedProperties = 1 << 10,

        All = CustomProperties | DisplayName | Alias | HasChildren | Category | ReservedProperties
    }
}