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
        Properties = 1 << 5 | Category,
        Alias = 1 << 6,
        Category = 1 << 7,
        DisplayName = 1 << 8 | Properties | Category,
        HasChildren = 1 << 9,
        MediaLibraryName = 1 << 11,
        Cache = 1 << 12,

        All = Properties | DisplayName | Alias | HasChildren | Category | MediaLibraryName | Cache
    }
}