using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.BulkModification.Components.Processors.Time;

public class BulkModificationTimeProcessor : AbstractBulkModificationProcessor<TimeSpan?,
    BulkModificationTimeProcessOperation, BulkModificationTimeProcessorOptions>
{
    public override StandardValueType ValueType => StandardValueType.Time;

    protected override TimeSpan? ProcessInternal(TimeSpan? currentValue,
        BulkModificationTimeProcessOperation operation,
        BulkModificationTimeProcessorOptions? options)
    {
        switch (operation)
        {
            case BulkModificationTimeProcessOperation.Delete:
                return null;
            case BulkModificationTimeProcessOperation.SetWithFixedValue:
                return options?.Value;
            case BulkModificationTimeProcessOperation.AddHours:
                return currentValue?.Add(TimeSpan.FromHours(options?.Amount ?? 0));
            case BulkModificationTimeProcessOperation.SubtractHours:
                return currentValue?.Subtract(TimeSpan.FromHours(options?.Amount ?? 0));
            case BulkModificationTimeProcessOperation.AddMinutes:
                return currentValue?.Add(TimeSpan.FromMinutes(options?.Amount ?? 0));
            case BulkModificationTimeProcessOperation.SubtractMinutes:
                return currentValue?.Subtract(TimeSpan.FromMinutes(options?.Amount ?? 0));
            case BulkModificationTimeProcessOperation.AddSeconds:
                return currentValue?.Add(TimeSpan.FromSeconds(options?.Amount ?? 0));
            case BulkModificationTimeProcessOperation.SubtractSeconds:
                return currentValue?.Subtract(TimeSpan.FromSeconds(options?.Amount ?? 0));
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }
}
