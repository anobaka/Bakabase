using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants
{
    public enum BulkModificationFilterOperation
    {
        Equals = 1,
        NotEquals,
        Contains,
        NotContains,
        StartsWith,
        NotStartsWith,
        EndsWith,
        NotEndsWith,
        GreaterThan,
        LessThan,
        GreaterThanOrEquals,
        LessThanOrEquals,
        IsNull,
        IsNotNull,
        In,
        NotIn,
        Matches,
        NotMatches
    }
}