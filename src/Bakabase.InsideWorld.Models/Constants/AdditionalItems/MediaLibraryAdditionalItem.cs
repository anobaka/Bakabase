using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Constants.AdditionalItems
{
    [Flags]
    public enum MediaLibraryAdditionalItem
    {
        None = 0,
        Category = 1,
        FileSystemInfo = 1 << 1,
        // FixedTags = 1 << 2
    }
}