using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.BulkModification.Components.Processors.Decimal;

public class BulkModificationDecimalProcessor : AbstractBulkModificationProcessor<decimal?,
    BulkModificationDecimalProcessOperation, BulkModificationDecimalProcessorOptions>
{
    public override StandardValueType ValueType => StandardValueType.Decimal;

    protected override decimal? ProcessInternal(decimal? currentValue,
        BulkModificationDecimalProcessOperation operation,
        BulkModificationDecimalProcessorOptions? options)
    {
        switch (operation)
        {
            case BulkModificationDecimalProcessOperation.Delete:
                return null;
            case BulkModificationDecimalProcessOperation.SetWithFixedValue:
                return options?.Value;
            case BulkModificationDecimalProcessOperation.Add:
                return currentValue.HasValue && options?.Value.HasValue == true
                    ? currentValue.Value + options.Value.Value
                    : options?.Value ?? currentValue;
            case BulkModificationDecimalProcessOperation.Subtract:
                return currentValue.HasValue && options?.Value.HasValue == true
                    ? currentValue.Value - options.Value.Value
                    : currentValue;
            case BulkModificationDecimalProcessOperation.Multiply:
                return currentValue.HasValue && options?.Value.HasValue == true
                    ? currentValue.Value * options.Value.Value
                    : currentValue;
            case BulkModificationDecimalProcessOperation.Divide:
                return currentValue.HasValue && options?.Value.HasValue == true && options.Value.Value != 0
                    ? currentValue.Value / options.Value.Value
                    : currentValue;
            case BulkModificationDecimalProcessOperation.Round:
                return currentValue.HasValue
                    ? Math.Round(currentValue.Value, options?.DecimalPlaces ?? 0)
                    : currentValue;
            case BulkModificationDecimalProcessOperation.Ceil:
                return currentValue.HasValue
                    ? Math.Ceiling(currentValue.Value)
                    : currentValue;
            case BulkModificationDecimalProcessOperation.Floor:
                return currentValue.HasValue
                    ? Math.Floor(currentValue.Value)
                    : currentValue;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }
}
