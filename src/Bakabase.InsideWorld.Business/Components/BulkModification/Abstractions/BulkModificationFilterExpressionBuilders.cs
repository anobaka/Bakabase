using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders;
using Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions
{
    internal class BulkModificationFilterExpressionBuilders
    {
        public static ConcurrentDictionary<BulkModificationProperty, IBulkModificationFilterExpressionBuilder>
            CustomBuilders =
                new(new Dictionary<BulkModificationProperty, IBulkModificationFilterExpressionBuilder>()
                {
                    {BulkModificationProperty.Tag, BmTagFilterExpressionBuilder.Instance}
                });

        public static IBulkModificationFilterExpressionBuilder DefaultExpressionBuilder =
            BmSimplePropertyFilterExpressionBuilder.Instance;
    }
}