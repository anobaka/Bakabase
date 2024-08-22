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
        public static ConcurrentDictionary<BulkModificationFilterableProperty, IBulkModificationFilterExpressionBuilder>
            Builders =
                new(new Dictionary<BulkModificationFilterableProperty, IBulkModificationFilterExpressionBuilder>()
                {
                    {BulkModificationFilterableProperty.Category, BmCategoryFilterExpressionBuilder.Instance},
                    {BulkModificationFilterableProperty.MediaLibrary, BmMediaLibraryFilterExpressionBuilder.Instance},
                    {BulkModificationFilterableProperty.FileName, BmFileNameFilterExpressionBuilder.Instance},
                    {BulkModificationFilterableProperty.DirectoryPath, BmDirectoryPathFilterExpressionBuilder.Instance},
                    {BulkModificationFilterableProperty.CreateDt, BmCreateDtFilterExpressionBuilder.Instance},
                    {BulkModificationFilterableProperty.FileCreateDt, BmFileCreateDtFilterExpressionBuilder.Instance},
                    {BulkModificationFilterableProperty.FileModifyDt, BmFileModifyDtFilterExpressionBuilder.Instance},
                });
    }
}