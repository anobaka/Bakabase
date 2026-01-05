using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.BulkModification.Components.Processors.DateTime;

public class BulkModificationDateTimeProcessor : AbstractBulkModificationProcessor<System.DateTime?,
    BulkModificationDateTimeProcessOperation, BulkModificationDateTimeProcessorOptions>
{
    public override StandardValueType ValueType => StandardValueType.DateTime;

    protected override System.DateTime? ProcessInternal(System.DateTime? currentValue,
        BulkModificationDateTimeProcessOperation operation,
        BulkModificationDateTimeProcessorOptions? options)
    {
        switch (operation)
        {
            case BulkModificationDateTimeProcessOperation.Delete:
                return null;
            case BulkModificationDateTimeProcessOperation.SetWithFixedValue:
                return options?.Value;
            case BulkModificationDateTimeProcessOperation.AddDays:
                return currentValue?.AddDays(options?.Amount ?? 0);
            case BulkModificationDateTimeProcessOperation.SubtractDays:
                return currentValue?.AddDays(-(options?.Amount ?? 0));
            case BulkModificationDateTimeProcessOperation.AddMonths:
                return currentValue?.AddMonths(options?.Amount ?? 0);
            case BulkModificationDateTimeProcessOperation.SubtractMonths:
                return currentValue?.AddMonths(-(options?.Amount ?? 0));
            case BulkModificationDateTimeProcessOperation.AddYears:
                return currentValue?.AddYears(options?.Amount ?? 0);
            case BulkModificationDateTimeProcessOperation.SubtractYears:
                return currentValue?.AddYears(-(options?.Amount ?? 0));
            case BulkModificationDateTimeProcessOperation.SetToNow:
                return System.DateTime.Now;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }
}
