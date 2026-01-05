using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Extensions;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.BulkModification.Components.Processors.ListTag;

public record BulkModificationListTagProcessOptions : AbstractBulkModificationProcessOptions<
    BulkModificationListTagProcessorOptions, BulkModificationListTagProcessOptionsViewModel>
{
    public BulkModificationProcessValue? Value { get; set; }
    public bool? IsOperationDirectionReversed { get; set; }

    public override void PopulateData(PropertyMap? propertyMap)
    {
        Value?.PopulateData(propertyMap);
    }

    protected override BulkModificationListTagProcessOptionsViewModel? ToViewModelInternal(
        IPropertyLocalizer? propertyLocalizer)
    {
        return new BulkModificationListTagProcessOptionsViewModel
        {
            Value = Value?.ToViewModel(propertyLocalizer),
            IsOperationDirectionReversed = IsOperationDirectionReversed
        };
    }

    protected override BulkModificationListTagProcessorOptions ConvertToProcessorOptionsInternal(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Dictionary<PropertyPool, Dictionary<int, Bakabase.Abstractions.Models.Domain.Property>>? propertyMap,
        IBulkModificationLocalizer localizer)
    {
        return new BulkModificationListTagProcessorOptions
        {
            Value = Value?.ConvertToStdValue<List<TagValue>>(StandardValueType.ListTag, variableMap, localizer),
            IsOperationDirectionReversed = IsOperationDirectionReversed
        };
    }
}
