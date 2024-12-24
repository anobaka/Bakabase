using Bakabase.Abstractions.Components.TextProcessing;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.Modules.BulkModification.Components.Processors.String
{
    public class BulkModificationStringProcessor : AbstractBulkModificationProcessor<string?, BulkModificationStringProcessOperation,
        BulkModificationStringProcessorOptions>
    {
        public override StandardValueType ValueType => StandardValueType.String;

        protected override string? ProcessInternal(string? currentValue, BulkModificationStringProcessOperation operation,
            BulkModificationStringProcessorOptions? options)
        {
            return currentValue.Process((TextProcessingOperation) operation, options);
        }
    }
}