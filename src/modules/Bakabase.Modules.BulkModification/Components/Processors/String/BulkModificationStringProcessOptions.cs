using Bakabase.Abstractions.Components.TextProcessing;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.Modules.BulkModification.Components.Processors.String;

public record
    BulkModificationStringProcessOptions : AbstractBulkModificationProcessOptions<
    BulkModificationStringProcessorOptions>
{
    public BulkModificationProcessorValueType? ValueType { get; set; }
    public string? Value { get; set; }
    public string? Find { get; set; }
    public string? Replace { get; set; }
    public int? Index { get; set; }
    public bool? IsPositioningDirectionReversed { get; set; }
    public bool? IsOperationDirectionReversed { get; set; }
    public int? Count { get; set; }

    protected override BulkModificationStringProcessorOptions ConvertToProcessorOptionsInternal(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Bakabase.Abstractions.Models.Domain.Property? property)
    {
        var options = new BulkModificationStringProcessorOptions
        {
            Find = Find,
            Replace = Replace,
            Index = Index,
            IsPositioningDirectionReversed = IsPositioningDirectionReversed,
            IsOperationDirectionReversed = IsOperationDirectionReversed,
            Count = Count,
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