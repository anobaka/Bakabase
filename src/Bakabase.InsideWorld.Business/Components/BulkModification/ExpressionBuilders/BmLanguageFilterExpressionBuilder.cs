using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders.Infrastructures;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders
{
    public class BmLanguageFilterExpressionBuilder : BmSingleValuePropertyFilterExpressionBuilder<ResourceLanguage>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.Language;
        protected override ResourceLanguage GetValue(ResourceDto resource)
        {
            return resource.Language;
        }
    }
}
