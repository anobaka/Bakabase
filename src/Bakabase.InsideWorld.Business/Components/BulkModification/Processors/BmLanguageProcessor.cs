using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures.BmSimpleValueProcessor;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors
{
    /// <summary>
    /// todo: remove <see cref="SpecialTextService"/> and use a new interface instead.
    /// </summary>
    /// <param name="specialTextService"></param>
    public class BmLanguageProcessor(SpecialTextService specialTextService)
        : BmCommonSimpleValueProcessor<ResourceLanguage>
    {
        protected override BmSimpleValueProcessorValue<string> ParseProcessorValue(BulkModificationProcess process) =>
            process.ToLanguageProcessorValue();

        protected override BulkModificationProperty TargetProperty => BulkModificationProperty.Language;

        protected override void SetValue(ResourceDto resource, ResourceLanguage value)
        {
            resource.Language = value;
        }

        protected override async Task<ResourceLanguage?> ParseTextToValue(string str) =>
            await specialTextService.TryToParseLanguage(str);

        protected override Task<ResourceLanguage?> TryParseStringValue(string str) =>
            Task.FromResult<ResourceLanguage?>(int.TryParse(str, out var number) ? (ResourceLanguage) number : null);

        protected override void RemoveValue(ResourceDto resource)
        {
            resource.Language = ResourceLanguage.NotSet;
        }
    }
}