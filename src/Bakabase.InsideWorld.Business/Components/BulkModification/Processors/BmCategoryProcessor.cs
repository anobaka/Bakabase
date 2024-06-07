using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors
{
    public class BmCategoryProcessor : BmAbstractBmProcessor<int>
    {
        protected override Task ProcessInternal(int value, Bakabase.Abstractions.Models.Domain.Resource resource,
            Dictionary<string, string?> variables,
            string? propertyKey)
        {
            resource.CategoryId = value;
            return Task.CompletedTask;
        }

        protected override int ParseProcessorValue(BulkModificationProcess process) =>
            process.ToCategoryProcessorValue();

        protected override BulkModificationFilterableProperty TargetProperty =>
            BulkModificationFilterableProperty.Category;
    }
}