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
    public class BmMediaLibraryProcessor : BmAbstractBmProcessor<int>
    {
        protected override Task ProcessInternal(int value, Models.Domain.Resource resource, Dictionary<string, string?> variables,
            string? propertyKey)
        {
            resource.MediaLibraryId = value;
            return Task.CompletedTask;
        }

        protected override int ParseProcessorValue(BulkModificationProcess process) =>
            process.ToMediaLibraryProcessorValue();

        protected override BulkModificationFilterableProperty TargetProperty => BulkModificationFilterableProperty.MediaLibrary;
    }
}