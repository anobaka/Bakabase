using Bakabase.Abstractions.Components.TextProcessing;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.Modules.BulkModification.Components.Processors
{
    public record BulkModificationTextProcessorOptions : TextProcessingOptions, IBulkModificationProcessorOptions;

    public record BulkModificationTextProcessOptions : TextProcessingOptions, IBulkModificationProcessOptions
    {
        public BulkModificationProcessorValueType? ValueType { get; set; }

        public IBulkModificationProcessorOptions? ConvertToProcessorOptions(
            Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
            Bakabase.Abstractions.Models.Domain.Property? property)
        {
            var options = new BulkModificationTextProcessorOptions
            {
                Find = Find,
                Replace = Replace,
                Position = Position,
                Reverse = Reverse,
                RemoveBefore = RemoveBefore,
                Count = Count,
                Start = Start,
                End = End,
            };

            if (ValueType.HasValue && Value.IsNotEmpty())
            {
                switch (ValueType.Value)
                {
                    case BulkModificationProcessorValueType.Static:
                    case BulkModificationProcessorValueType.Dynamic:
                        options.Value = Value?.DeserializeAsStandardValue(StandardValueType.String) as string;
                        break;
                    case BulkModificationProcessorValueType.Variable:
                        if (variableMap?.TryGetValue(Value, out var tv) == true)
                        {
                            var stdHandler = tv.Type.GetHandler();
                            options.Value = stdHandler.Convert(Value.DeserializeAsStandardValue(tv.Type),
                                StandardValueType.String) as string;
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return options;
        }
    }

    public class BmTextProcessor : AbstractBulkModificationProcessor<string?, TextProcessingOperation,
        BulkModificationTextProcessorOptions>
    {
        public override StandardValueType ValueType => StandardValueType.String;

        protected override string? ProcessInternal(string? currentValue, TextProcessingOperation operation,
            BulkModificationTextProcessorOptions? options)
        {
            return currentValue.Process(operation, options);
        }
    }
}