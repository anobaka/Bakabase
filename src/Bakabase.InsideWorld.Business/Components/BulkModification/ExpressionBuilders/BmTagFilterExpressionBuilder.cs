using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders.Infrastructures;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders
{
    public class BmTagFilterExpressionBuilder : BmMultipleValuePropertyFilterExpressionBuilder<string>
    {
        public static BmTagFilterExpressionBuilder Instance = new();
        protected override BulkModificationFilterableProperty Property => BulkModificationFilterableProperty.Tag;

        protected override HashSet<string>? GetValue(Models.Domain.Resource resource, BulkModificationFilter filter)
        {
            if (resource.Tags?.Any() != true)
            {
                return null;
            }

            var names = resource.Tags.Select(x => x.Name).ToHashSet();
            var aliases = resource.Tags.Select(x => x.PreferredAlias).Where(a => !string.IsNullOrEmpty(a)).ToHashSet()!;

            return names.Concat(aliases).ToHashSet()!;
        }
    }
}