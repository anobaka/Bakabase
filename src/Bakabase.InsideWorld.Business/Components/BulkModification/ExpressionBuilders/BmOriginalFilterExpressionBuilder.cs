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
    public class BmOriginalFilterExpressionBuilder : BmMultipleValuePropertyFilterExpressionBuilder<string>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.Original;

        protected override HashSet<string>? GetValue(ResourceDto resource, BulkModificationFilter filter)
        {
            return resource.Originals?.Where(x => !string.IsNullOrEmpty(x.Name)).Select(x => x.Name).ToHashSet();
        }
    }
}