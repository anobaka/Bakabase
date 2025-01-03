using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;
using Bakabase.Modules.BulkModification.Components.Processors.String;
using Bakabase.Modules.BulkModification.Extensions;
using Bakabase.Modules.BulkModification.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
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
    BulkModificationListStringProcessorOptions, BulkModificationListStringProcessOptionsViewModel>
{
    public BulkModificationProcessValue? Value { get; set; }
    public bool? IsOperationDirectionReversed { get; set; }
    public BulkModificationListStringProcessModifyOptions? ModifyOptions { get; set; }

    public override void PopulateData(PropertyMap? propertyMap)
    {
        Value?.PopulateData(propertyMap);
        ModifyOptions?.Options?.PopulateData(propertyMap);
    }

    protected override BulkModificationListStringProcessOptionsViewModel? ToViewModelInternal(
        IPropertyLocalizer? propertyLocalizer)
    {
        return new BulkModificationListStringProcessOptionsViewModel
        {
            IsOperationDirectionReversed = IsOperationDirectionReversed,
            Value = Value?.ToViewModel(propertyLocalizer),
            ModifyOptions = ModifyOptions == null
                ? null
                : new BulkModificationListStringProcessModifyOptionsViewModel
                {
                    FilterBy = ModifyOptions.FilterBy,
                    FilterValue = ModifyOptions.FilterValue,
                    Operation = ModifyOptions.Operation,
                    Options =
                        ModifyOptions.Options?.ToViewModel(propertyLocalizer) as
                            BulkModificationStringProcessOptionsViewModel
                }
        };
    }

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
            Value = Value?.ConvertToStdValue<List<string>>(StandardValueType.ListString, variableMap, localizer)
        };

        return options;
    }
}