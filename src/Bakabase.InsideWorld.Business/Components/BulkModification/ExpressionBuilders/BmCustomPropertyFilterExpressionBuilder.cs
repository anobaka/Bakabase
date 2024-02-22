using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders.Infrastructures;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders
{
    public class BmCustomPropertyFilterExpressionBuilder: BmMultipleValuePropertyFilterExpressionBuilder<string>
    {
        public static BmCustomPropertyFilterExpressionBuilder Instance = new();

        protected override BulkModificationProperty Property => BulkModificationProperty.CustomProperty;
        protected override HashSet<string>? GetValue(ResourceDto resource, BulkModificationFilter filter)
        {
            return resource.CustomProperties?.Where(x => x.Key == filter.PropertyKey && x.Value?.Any() == true)
                .SelectMany(x => x.Value.Select(a => a.Value)).ToHashSet();
        }
    }
}
