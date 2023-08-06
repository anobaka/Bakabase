using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Constants.AdditionalItems
{
    [Flags]
    public enum TagGroupAdditionalItem
    {
        Tags = 1,
        PreferredAlias = 2,
        TagNamePreferredAlias = 4
    }
}