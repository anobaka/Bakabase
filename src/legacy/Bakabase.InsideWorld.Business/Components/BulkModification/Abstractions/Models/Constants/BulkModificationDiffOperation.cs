using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants
{
    public enum BulkModificationDiffOperation
    {
        None = 0,
        Ignore = 1,
        Replace = 2,
        Merge = 3
    }
}
