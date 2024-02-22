using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures;
using Bakabase.InsideWorld.Business.Components.TextProcessing;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors
{
    public class BmNameProcessor : BmTextProcessor
    {
        protected override TextProcessValue ParseProcessorValue(BulkModificationProcess process) =>
            process.ToNameProcessorValue();

        protected override BulkModificationProperty TargetProperty => BulkModificationProperty.Name;
        protected override string? GetValue(ResourceDto resource, string? propertyKey) => resource.Name;

        protected override void SetValue(ResourceDto resource, string? propertyKey, string? value)
        {
            resource.Name = value;
        }
    }
}