using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Components.Processors.String;
using Bakabase.Modules.BulkModification.Models.Domain.Constants;
using Bootstrap.Extensions;
using NPOI.SS.Formula.Atp;

namespace Bakabase.Modules.BulkModification.Components.Processors.ListString;

public class BulkModificationListStringProcessor : AbstractBulkModificationProcessor<List<string>,
    BulkModificationListStringProcessOperation, BulkModificationListStringProcessorOptions>
{
    public override StandardValueType ValueType => StandardValueType.ListString;

    protected override List<string>? ProcessInternal(List<string>? currentValue,
        BulkModificationListStringProcessOperation operation,
        BulkModificationListStringProcessorOptions? options)
    {
        switch (operation)
        {
            case BulkModificationListStringProcessOperation.SetWithFixedValue:
                return options?.Value;
            case BulkModificationListStringProcessOperation.Append:
            {
                return options?.Value != null
                    ? currentValue == null ? options.Value : currentValue.Concat(options.Value).ToList()
                    : currentValue;
            }
            case BulkModificationListStringProcessOperation.Prepend:
                return options?.Value != null
                    ? currentValue == null ? options.Value : options.Value.Concat(currentValue).ToList()
                    : currentValue;
            case BulkModificationListStringProcessOperation.Modify:
            {
                if (options?.ModifyOptions != null && currentValue?.Any() == true)
                {
                    var newValue = new List<string>();
                    foreach (var item in currentValue)
                    {
                        var toBeProcessed = options.ModifyOptions.FilterBy switch
                        {
                            BulkModificationProcessorOptionsItemsFilterBy.All => true,
                            BulkModificationProcessorOptionsItemsFilterBy.Containing => options.ModifyOptions
                                .FilterValue.IsNotEmpty() && item.Contains(options.ModifyOptions.FilterValue),
                            BulkModificationProcessorOptionsItemsFilterBy.Matching => options.ModifyOptions.FilterValue
                                .IsNotEmpty() && Regex.IsMatch(item, options.ModifyOptions.FilterValue),
                            _ => throw new ArgumentOutOfRangeException()
                        };

                        var newItem = item;
                        if (toBeProcessed)
                        {
                            newItem = BulkModificationInternals.StringProcessor.Process(item,
                                (int) options.ModifyOptions.Operation, options.ModifyOptions.Options) as string;
                        }

                        newItem = newItem?.Trim();
                        if (newItem.IsNotEmpty())
                        {
                            newValue.Add(newItem);
                        }
                    }

                    return newValue;
                }

                return currentValue;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }
}