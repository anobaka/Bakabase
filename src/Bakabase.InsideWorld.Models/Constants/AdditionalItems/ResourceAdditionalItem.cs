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
        Publishers = 1,
        Volume = 1 << 1,
        Serial = 1 << 2 | Volume,
        Originals = 1 << 3,
        Tags = 1 << 4,
        CustomProperties = 1 << 5,
        Alias = 1 << 6,

        All = Publishers | Volume | Serial | Originals | Tags | CustomProperties | Alias
    }
}