using System;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures.BmSimpleValueProcessor;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors;

/// <summary>
/// todo: remove <see cref="SpecialTextService"/> and use a new interface instead.
/// </summary>
/// <param name="specialTextService"></param>
public class BmReleaseDtProcessor(SpecialTextService specialTextService)
    : BmCommonSimpleValueProcessor<DateTime>
{
    protected override BmSimpleValueProcessorValue<string> ParseProcessorValue(BulkModificationProcess process) =>
        process.ToReleaseDtProcessorValue();

    protected override BulkModificationProperty TargetProperty => BulkModificationProperty.ReleaseDt;

    protected override void SetValue(ResourceDto resource, DateTime value)
    {
        resource.ReleaseDt = value;
    }

    protected override async Task<DateTime?> ParseTextToValue(string str) =>
        await specialTextService.TryToParseDateTime(str);

    protected override async Task<DateTime?> TryParseStringValue(string? str)
    {
        return await specialTextService.TryToParseDateTime(str);
    }

    protected override void RemoveValue(ResourceDto resource)
    {
        resource.ReleaseDt = null;
    }
}