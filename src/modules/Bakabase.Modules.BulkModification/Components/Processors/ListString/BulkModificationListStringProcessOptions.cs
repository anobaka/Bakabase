using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
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
    public BulkModificationProcessValue? Value { get; set; }
    public bool? IsOperationDirectionReversed { get; set; }
    public BulkModificationListStringProcessModifyOptions? ModifyOptions { get; set; }

    protected override BulkModificationListStringProcessorOptions ConvertToProcessorOptionsInternal(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Dictionary<PropertyPool, Dictionary<int, Bakabase.Abstractions.Models.Domain.Property>>? propertyMap,
        IBulkModificationLocalizer localizer)
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
                        ModifyOptions.Options?.ConvertToProcessorOptions(variableMap, propertyMap, localizer) as
                            BulkModificationStringProcessorOptions,
                    Operation = ModifyOptions.Operation
                },
            Value = Value?.ConvertToStdValue<List<string>>(StandardValueType.ListString, variableMap, propertyMap,
                localizer)
        };

        return options;
    }
}