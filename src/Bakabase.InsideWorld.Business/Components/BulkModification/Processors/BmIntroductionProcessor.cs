using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures;
using Bakabase.InsideWorld.Business.Components.TextProcessing;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors;

public class BmIntroductionProcessor : BmTextProcessor
{
    protected override TextProcessValue ParseProcessorValue(BulkModificationProcess process) =>
        process.ToIntroductionProcessorValue();

    protected override BulkModificationProperty TargetProperty => BulkModificationProperty.Introduction;
    protected override string? GetValue(ResourceDto resource, string? propertyKey) => resource.Introduction;

    protected override void SetValue(ResourceDto resource, string? propertyKey, string? value)
    {
        resource.Introduction = value;
    }
}