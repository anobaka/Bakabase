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
    public class BmDirectoryPathFilterExpressionBuilder : BmSingleValuePropertyFilterExpressionBuilder<string>
    {
        public static BmDirectoryPathFilterExpressionBuilder Instance = new();

        protected override BulkModificationFilterableProperty Property => BulkModificationFilterableProperty.DirectoryPath;
        protected override string? GetValue(Models.Domain.Resource resource)
        {
            return resource.Directory;
        }
    }
}
