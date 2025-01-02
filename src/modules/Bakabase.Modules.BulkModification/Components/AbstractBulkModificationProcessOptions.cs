using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.Modules.BulkModification.Components;

public abstract record
    AbstractBulkModificationProcessOptions<TProcessorOptions,
        TProcessOptionsViewModel> : IBulkModificationProcessOptions
    where TProcessorOptions : IBulkModificationProcessorOptions
{
    public IBulkModificationProcessorOptions? ConvertToProcessorOptions(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Dictionary<PropertyPool, Dictionary<int, Bakabase.Abstractions.Models.Domain.Property>>? propertyMap,
        IBulkModificationLocalizer localizer)
    {
        return ConvertToProcessorOptionsInternal(variableMap, propertyMap, localizer);
    }

    public abstract void PopulateData(PropertyMap? propertyMap);

    public object? ToViewModel(IPropertyLocalizer? propertyLocalizer) => ToViewModelInternal(propertyLocalizer);

    protected abstract TProcessOptionsViewModel? ToViewModelInternal(IPropertyLocalizer? propertyLocalizer);

    protected abstract TProcessorOptions? ConvertToProcessorOptionsInternal(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Dictionary<PropertyPool, Dictionary<int, Bakabase.Abstractions.Models.Domain.Property>>? propertyMap,
        IBulkModificationLocalizer localizer);
}