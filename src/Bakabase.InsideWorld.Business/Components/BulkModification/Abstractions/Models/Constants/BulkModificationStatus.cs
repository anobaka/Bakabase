using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants
{
    public enum BulkModificationStatus
    {
        Initial = 1,
        Filtered = 2,
        Complete = 3,
        Failed = 4,
        Cancelled = 5
    }
}