using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Extensions;
using Bakabase.Modules.Property.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Components.Processors.DateTime;

public record BulkModificationDateTimeProcessOptions : AbstractBulkModificationProcessOptions<
    BulkModificationDateTimeProcessorOptions, BulkModificationDateTimeProcessOptionsViewModel>
{
    public BulkModificationProcessValue? Value { get; set; }
    public int? Amount { get; set; }

    public override void PopulateData(PropertyMap? propertyMap)
    {
        Value?.PopulateData(propertyMap);
    }

    protected override BulkModificationDateTimeProcessOptionsViewModel? ToViewModelInternal(
        IPropertyLocalizer? propertyLocalizer)
    {
        return new BulkModificationDateTimeProcessOptionsViewModel
        {
            Value = Value?.ToViewModel(propertyLocalizer),
            Amount = Amount
        };
    }

    protected override BulkModificationDateTimeProcessorOptions ConvertToProcessorOptionsInternal(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Dictionary<PropertyPool, Dictionary<int, Bakabase.Abstractions.Models.Domain.Property>>? propertyMap,
        IBulkModificationLocalizer localizer)
    {
        return new BulkModificationDateTimeProcessorOptions
        {
            Value = Value?.ConvertToStdValue<System.DateTime>(StandardValueType.DateTime, variableMap, localizer),
            Amount = Amount
        };
    }
}
