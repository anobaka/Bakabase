using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.BulkModification.Components.Processors.ListTag;

public class BulkModificationListTagProcessor : AbstractBulkModificationProcessor<List<TagValue>?,
    BulkModificationListTagProcessOperation, BulkModificationListTagProcessorOptions>
{
    public override StandardValueType ValueType => StandardValueType.ListTag;

    protected override List<TagValue>? ProcessInternal(List<TagValue>? currentValue,
        BulkModificationListTagProcessOperation operation,
        BulkModificationListTagProcessorOptions? options)
    {
        switch (operation)
        {
            case BulkModificationListTagProcessOperation.Delete:
                return null;
            case BulkModificationListTagProcessOperation.SetWithFixedValue:
                return options?.Value;
            case BulkModificationListTagProcessOperation.Append:
                if (options?.Value == null || options.Value.Count == 0)
                    return currentValue;
                return currentValue == null
                    ? options.Value
                    : currentValue.Concat(options.Value).ToList();
            case BulkModificationListTagProcessOperation.Prepend:
                if (options?.Value == null || options.Value.Count == 0)
                    return currentValue;
                return currentValue == null
                    ? options.Value
                    : options.Value.Concat(currentValue).ToList();
            case BulkModificationListTagProcessOperation.Remove:
                if (currentValue == null || options?.Value == null || options.Value.Count == 0)
                    return currentValue;
                return currentValue.Where(item =>
                    !options.Value.Any(toRemove => TagValue.GroupNameComparer.Equals(item, toRemove))).ToList();
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }
}
