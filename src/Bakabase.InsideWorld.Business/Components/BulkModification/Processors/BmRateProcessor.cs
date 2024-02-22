using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures.BmSimpleValueProcessor;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors
{
    public class BmRateProcessor : BmCommonSimpleValueProcessor<decimal>
    {
        protected override BmSimpleValueProcessorValue<string> ParseProcessorValue(BulkModificationProcess process) =>
            process.ToRateProcessorValue();

        protected override BulkModificationProperty TargetProperty => BulkModificationProperty.Rate;

        protected override void SetValue(ResourceDto resource, decimal value)
        {
            resource.Rate = value;
        }

        protected override Task<decimal?> ParseTextToValue(string str) =>
            Task.FromResult<decimal?>(decimal.TryParse(str, out var value) ? value : null);

        protected override Task<decimal?> TryParseStringValue(string str) =>
            Task.FromResult<decimal?>(decimal.TryParse(str, out var value) ? value : null);

        protected override void RemoveValue(ResourceDto resource)
        {
            resource.Rate = 0;
        }
    }
}