using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Components.Processors.String;
using Bakabase.Modules.BulkModification.Extensions;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.BulkModification.Components.Processors.Link;

public record BulkModificationLinkProcessOptions : AbstractBulkModificationProcessOptions<
    BulkModificationLinkProcessorOptions, BulkModificationLinkProcessOptionsViewModel>
{
    public BulkModificationProcessValue? Value { get; set; }
    public BulkModificationProcessValue? Text { get; set; }
    public BulkModificationProcessValue? Url { get; set; }
    public BulkModificationStringProcessOperation? StringOperation { get; set; }
    public BulkModificationStringProcessOptions? StringOptions { get; set; }

    public override void PopulateData(PropertyMap? propertyMap)
    {
        Value?.PopulateData(propertyMap);
        Text?.PopulateData(propertyMap);
        Url?.PopulateData(propertyMap);
        StringOptions?.PopulateData(propertyMap);
    }

    protected override BulkModificationLinkProcessOptionsViewModel? ToViewModelInternal(
        IPropertyLocalizer? propertyLocalizer)
    {
        return new BulkModificationLinkProcessOptionsViewModel
        {
            Value = Value?.ToViewModel(propertyLocalizer),
            Text = Text?.ToViewModel(propertyLocalizer),
            Url = Url?.ToViewModel(propertyLocalizer),
            StringOperation = StringOperation,
            StringOptions = StringOptions?.ToViewModel(propertyLocalizer) as BulkModificationStringProcessOptionsViewModel
        };
    }

    protected override BulkModificationLinkProcessorOptions ConvertToProcessorOptionsInternal(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Dictionary<PropertyPool, Dictionary<int, Bakabase.Abstractions.Models.Domain.Property>>? propertyMap,
        IBulkModificationLocalizer localizer)
    {
        return new BulkModificationLinkProcessorOptions
        {
            Value = Value?.ConvertToStdValue<LinkValue>(StandardValueType.Link, variableMap, localizer),
            Text = Text?.ConvertToStdValue<string>(StandardValueType.String, variableMap, localizer),
            Url = Url?.ConvertToStdValue<string>(StandardValueType.String, variableMap, localizer),
            StringOperation = StringOperation,
            StringOptions = StringOptions?.ConvertToProcessorOptions(variableMap, propertyMap, localizer) as BulkModificationStringProcessorOptions
        };
    }
}
