using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;
using Bakabase.Modules.BulkModification.Components.Processors.String;
using Bakabase.Modules.BulkModification.Models.Domain.Constants;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.Modules.BulkModification.Components.Processors.ListString;

public record BulkModificationListStringProcessModifyOptions
{
    public BulkModificationProcessorOptionsItemsFilterBy FilterBy { get; set; }
    public string? FilterValue { get; set; }
    public BulkModificationStringProcessOperation Operation { get; set; }
    public BulkModificationStringProcessOptions? Options { get; set; }
}

public record
    BulkModificationListStringProcessOptions : AbstractBulkModificationProcessOptions<
    BulkModificationListStringProcessorOptions>
{
    public string? Value { get; set; }
    public BulkModificationProcessorValueType? ValueType { get; set; }
    public bool? IsOperationDirectionReversed { get; set; }
    public BulkModificationListStringProcessModifyOptions? ModifyOptions { get; set; }

    protected override BulkModificationListStringProcessorOptions ConvertToProcessorOptionsInternal(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Bakabase.Abstractions.Models.Domain.Property? property)
    {
        var options = new BulkModificationListStringProcessorOptions
        {
            IsOperationDirectionReversed = IsOperationDirectionReversed,
            ModifyOptions = ModifyOptions == null
                ? null
                : new BulkModificationListStringProcessorModifyOptions
                {
                    FilterBy = ModifyOptions.FilterBy,
                    FilterValue = ModifyOptions.FilterValue,
                    Options =
                        ModifyOptions.Options?.ConvertToProcessorOptions(variableMap, property) as
                            BulkModificationStringProcessorOptions,
                    Operation = ModifyOptions.Operation
                }
        };

        if (ValueType.HasValue && Value.IsNotEmpty())
        {
            switch (ValueType.Value)
            {
                case BulkModificationProcessorValueType.Static:
                    options.Value = Value?.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString);
                    break;
                case BulkModificationProcessorValueType.Dynamic:
                    var dbValue = Value?.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString);
                    options.Value = property?.GetBizValue<List<string>>(dbValue);
                    break;
                case BulkModificationProcessorValueType.Variable:
                    if (variableMap?.TryGetValue(Value, out var tv) == true)
                    {
                        var stdHandler = tv.Type.GetHandler();
                        options.Value = stdHandler.Convert(Value.DeserializeAsStandardValue(tv.Type),
                            StandardValueType.String) as List<string>;
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return options;
    }
}