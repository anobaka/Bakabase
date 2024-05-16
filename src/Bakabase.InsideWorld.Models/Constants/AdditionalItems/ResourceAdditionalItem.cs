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
        [Obsolete] Publishers = 1,
        [Obsolete] Volume = 1 << 1,
        [Obsolete] Serial = 1 << 2 | Volume,
        [Obsolete] Originals = 1 << 3,
        [Obsolete] Tags = 1 << 4,
        CustomProperties = 1 << 5,
        Alias = 1 << 6,
        Category = 1 << 7,
        DisplayName = 1 << 8 | CustomProperties | Category,
        HasChildren = 1 << 9,

        // All = Publishers | Volume | Serial | Originals | Tags | CustomProperties | Alias
        All = CustomProperties | DisplayName | Alias | HasChildren
    }
}