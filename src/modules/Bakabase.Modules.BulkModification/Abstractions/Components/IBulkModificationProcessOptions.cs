using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;
using Bakabase.Modules.Property.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Abstractions.Components;

/// <summary>
/// Options for a bulk modification process which may have more concepts than <see cref="IBulkModificationProcessorOptions"/> such as <see cref="BulkModificationProcessorValueType"/>.
/// </summary>
public interface IBulkModificationProcessOptions
{
    IBulkModificationProcessorOptions? ConvertToProcessorOptions(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Dictionary<PropertyPool, Dictionary<int, Bakabase.Abstractions.Models.Domain.Property>>? propertyMap,
        IBulkModificationLocalizer localizer);

    void PopulateData(PropertyMap? propertyMap);

    /// <summary>
    /// Not a good abstraction, but easy to make sure view model conversion is implemented.
    /// </summary>
    /// <returns></returns>
    object? ToViewModel(IPropertyLocalizer? propertyLocalizer);
}