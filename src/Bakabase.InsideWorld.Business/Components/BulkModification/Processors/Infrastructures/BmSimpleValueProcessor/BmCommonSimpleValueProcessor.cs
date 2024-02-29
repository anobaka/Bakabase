using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures.BmSimpleValueProcessor
{
    public abstract class
        BmCommonSimpleValueProcessor<TPropertyType> : BmAbstractBmProcessor<
        BmSimpleValueProcessorValue<string>> where TPropertyType : struct
    {
        protected abstract void SetValue(ResourceDto resource, TPropertyType value);
        protected abstract Task<TPropertyType?> ParseTextToValue(string str);
        protected abstract Task<TPropertyType?> TryParseStringValue(string str);
        protected abstract void RemoveValue(ResourceDto resource);

        protected override async Task ProcessInternal(BmSimpleValueProcessorValue<string> value, ResourceDto resource,
            Dictionary<string, string?> variables, string? propertyKey)
        {
            switch (value.Operation)
            {
                case BmSimpleValueProcessorOperation.SetWithFixedValue:
                {
                    if (!string.IsNullOrEmpty(value.Value))
                    {
                        var v = await TryParseStringValue(value.Value);
                        if (v.HasValue)
                        {
                            SetValue(resource, v.Value);
                        }
                    }

                    break;
                }
                case BmSimpleValueProcessorOperation.SetWithDynamicValue:
                {
                    if (!string.IsNullOrEmpty(value.Value))
                    {
                        var str = value.Value;
                        if (value.Value.Contains(BulkModificationConstants.VariableStart))
                        {
                            foreach (var variable in variables)
                            {
                                str = str.Replace(
                                    $"{BulkModificationConstants.VariableStart}{variable.Key}{BulkModificationConstants.VariableEnd}",
                                    variable.Value);
                            }
                        }

                        var v = await ParseTextToValue(str);
                        if (v != null)
                        {
                            SetValue(resource, v.Value);
                        }
                    }

                    break;
                }
                case BmSimpleValueProcessorOperation.Remove:
                    RemoveValue(resource);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}