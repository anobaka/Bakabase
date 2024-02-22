using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders.Infrastructures;
using Bakabase.InsideWorld.Models.Extensions;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders
{
    public class BmPublisherFilterExpressionBuilder : BmMultipleValuePropertyFilterExpressionBuilder<string>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.Publisher;

        protected override HashSet<string>? GetValue(ResourceDto resource, BulkModificationFilter filter)
        {
            return resource.Publishers?.Extract().Select(x => x.Name).ToHashSet();
        }
    }
}