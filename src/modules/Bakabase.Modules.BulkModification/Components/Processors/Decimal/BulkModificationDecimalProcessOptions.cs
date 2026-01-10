using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Extensions;
using Bakabase.Modules.Property.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Components.Processors.Decimal;

public record BulkModificationDecimalProcessOptions : AbstractBulkModificationProcessOptions<
    BulkModificationDecimalProcessorOptions, BulkModificationDecimalProcessOptionsViewModel>
{
    public BulkModificationProcessValue? Value { get; set; }
    public int? DecimalPlaces { get; set; }

    public override void PopulateData(PropertyMap? propertyMap)
    {
        Value?.PopulateData(propertyMap);
    }

    protected override BulkModificationDecimalProcessOptionsViewModel? ToViewModelInternal(
        IPropertyLocalizer? propertyLocalizer)
    {
        return new BulkModificationDecimalProcessOptionsViewModel
        {
            Value = Value?.ToViewModel(propertyLocalizer),
            DecimalPlaces = DecimalPlaces
        };
    }

    protected override BulkModificationDecimalProcessorOptions ConvertToProcessorOptionsInternal(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Dictionary<PropertyPool, Dictionary<int, Bakabase.Abstractions.Models.Domain.Property>>? propertyMap,
        IBulkModificationLocalizer localizer)
    {
        return new BulkModificationDecimalProcessorOptions
        {
            Value = Value?.ConvertToStdValue<decimal>(StandardValueType.Decimal, variableMap, localizer),
            DecimalPlaces = DecimalPlaces
        };
    }
}
