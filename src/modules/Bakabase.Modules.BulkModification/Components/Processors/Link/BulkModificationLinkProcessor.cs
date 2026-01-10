using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.BulkModification.Components.Processors.Link;

public class BulkModificationLinkProcessor : AbstractBulkModificationProcessor<LinkValue?,
    BulkModificationLinkProcessOperation, BulkModificationLinkProcessorOptions>
{
    public override StandardValueType ValueType => StandardValueType.Link;

    protected override LinkValue? ProcessInternal(LinkValue? currentValue,
        BulkModificationLinkProcessOperation operation,
        BulkModificationLinkProcessorOptions? options)
    {
        switch (operation)
        {
            case BulkModificationLinkProcessOperation.Delete:
                return null;
            case BulkModificationLinkProcessOperation.SetWithFixedValue:
                return options?.Value;
            case BulkModificationLinkProcessOperation.SetText:
                return currentValue == null
                    ? new LinkValue(options?.Text, null)
                    : currentValue with { Text = options?.Text };
            case BulkModificationLinkProcessOperation.SetUrl:
                return currentValue == null
                    ? new LinkValue(null, options?.Url)
                    : currentValue with { Url = options?.Url };
            case BulkModificationLinkProcessOperation.ModifyText:
                if (currentValue == null || options?.StringOperation == null)
                    return currentValue;
                var newText = BulkModificationInternals.StringProcessor.Process(
                    currentValue.Text, (int)options.StringOperation, options.StringOptions) as string;
                return currentValue with { Text = newText };
            case BulkModificationLinkProcessOperation.ModifyUrl:
                if (currentValue == null || options?.StringOperation == null)
                    return currentValue;
                var newUrl = BulkModificationInternals.StringProcessor.Process(
                    currentValue.Url, (int)options.StringOperation, options.StringOptions) as string;
                return currentValue with { Url = newUrl };
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }
}
