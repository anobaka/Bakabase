using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions
{
    internal class BulkModificationFilterExpressionBuilders
    {
        public static ConcurrentDictionary<BulkModificationFilterProperty, IBulkModificationFilterExpressionBuilder> Builders =
            new();
    }
}
