using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.BulkModification.Components.Processors.ListListString;

public class BulkModificationListListStringProcessor : AbstractBulkModificationProcessor<List<List<string>>?,
    BulkModificationListListStringProcessOperation, BulkModificationListListStringProcessorOptions>
{
    public override StandardValueType ValueType => StandardValueType.ListListString;

    protected override List<List<string>>? ProcessInternal(List<List<string>>? currentValue,
        BulkModificationListListStringProcessOperation operation,
        BulkModificationListListStringProcessorOptions? options)
    {
        switch (operation)
        {
            case BulkModificationListListStringProcessOperation.Delete:
                return null;
            case BulkModificationListListStringProcessOperation.SetWithFixedValue:
                return options?.Value;
            case BulkModificationListListStringProcessOperation.Append:
                if (options?.Value == null || options.Value.Count == 0)
                    return currentValue;
                return currentValue == null
                    ? options.Value
                    : currentValue.Concat(options.Value).ToList();
            case BulkModificationListListStringProcessOperation.Prepend:
                if (options?.Value == null || options.Value.Count == 0)
                    return currentValue;
                return currentValue == null
                    ? options.Value
                    : options.Value.Concat(currentValue).ToList();
            case BulkModificationListListStringProcessOperation.Remove:
                if (currentValue == null || options?.Value == null || options.Value.Count == 0)
                    return currentValue;
                return currentValue.Where(item =>
                    !options.Value.Any(toRemove => SequenceEqual(item, toRemove))).ToList();
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static bool SequenceEqual(List<string>? a, List<string>? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.SequenceEqual(b);
    }
}
