using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Extensions;
using Bakabase.Modules.Property.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Components.Processors.Boolean;

public record BulkModificationBooleanProcessOptions : AbstractBulkModificationProcessOptions<
    BulkModificationBooleanProcessorOptions, BulkModificationBooleanProcessOptionsViewModel>
{
    public BulkModificationProcessValue? Value { get; set; }

    public override void PopulateData(PropertyMap? propertyMap)
    {
        Value?.PopulateData(propertyMap);
    }

    protected override BulkModificationBooleanProcessOptionsViewModel? ToViewModelInternal(
        IPropertyLocalizer? propertyLocalizer)
    {
        return new BulkModificationBooleanProcessOptionsViewModel
        {
            Value = Value?.ToViewModel(propertyLocalizer)
        };
    }

    protected override BulkModificationBooleanProcessorOptions ConvertToProcessorOptionsInternal(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Dictionary<PropertyPool, Dictionary<int, Bakabase.Abstractions.Models.Domain.Property>>? propertyMap,
        IBulkModificationLocalizer localizer)
    {
        return new BulkModificationBooleanProcessorOptions
        {
            Value = Value?.ConvertToStdValue<bool>(StandardValueType.Boolean, variableMap, localizer)
        };
    }
}
