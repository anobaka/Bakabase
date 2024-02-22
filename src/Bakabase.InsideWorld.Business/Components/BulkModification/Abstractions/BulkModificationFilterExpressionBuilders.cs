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
            Builders =
                new(new Dictionary<BulkModificationProperty, IBulkModificationFilterExpressionBuilder>()
                {
                    {BulkModificationProperty.Category, BmCategoryFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.MediaLibrary, BmMediaLibraryFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.Name, BmNameFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.FileName, BmFileNameFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.DirectoryPath, BmDirectoryPathFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.ReleaseDt, BmReleaseDtFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.CreateDt, BmCreateDtFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.FileCreateDt, BmFileCreateDtFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.FileModifyDt, BmFileModifyDtFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.Publisher, BmPublisherFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.Language, BmLanguageFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.Volume, BmVolumeFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.Original, BmOriginalFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.Series, BmSeriesFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.Tag, BmTagFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.Introduction, BmIntroductionFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.Rate, BmRateFilterExpressionBuilder.Instance},
                    {BulkModificationProperty.CustomProperty, BmCustomPropertyFilterExpressionBuilder.Instance},
                });
    }
}