using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.BulkModification.Components.Processors.Boolean;

public class BulkModificationBooleanProcessor : AbstractBulkModificationProcessor<bool?,
    BulkModificationBooleanProcessOperation, BulkModificationBooleanProcessorOptions>
{
    public override StandardValueType ValueType => StandardValueType.Boolean;

    protected override bool? ProcessInternal(bool? currentValue,
        BulkModificationBooleanProcessOperation operation,
        BulkModificationBooleanProcessorOptions? options)
    {
        return operation switch
        {
            BulkModificationBooleanProcessOperation.Delete => null,
            BulkModificationBooleanProcessOperation.SetWithFixedValue => options?.Value,
            BulkModificationBooleanProcessOperation.Toggle => currentValue.HasValue ? !currentValue.Value : true,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
    }
}
