using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders.Infrastructures;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders
{
    public class BmFileCreateDtFilterExpressionBuilder : BmSingleValuePropertyFilterExpressionBuilder<DateTime?>
    {
        public static BmFileCreateDtFilterExpressionBuilder Instance = new();

        protected override BulkModificationFilterableProperty Property => BulkModificationFilterableProperty.FileCreateDt;
        protected override DateTime? GetValue(Models.Domain.Resource resource)
        {
            return resource.FileCreateDt;
        }
    }
}
