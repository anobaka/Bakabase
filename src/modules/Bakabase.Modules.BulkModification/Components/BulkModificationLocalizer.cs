using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Components;
using Microsoft.Extensions.Localization;

namespace Bakabase.Modules.BulkModification.Components;

internal class BulkModificationLocalizer(
    IStringLocalizer<BulkModificationResource> localizer,
    IBakabaseLocalizer bakabaseLocalizer,
    IPropertyLocalizer propertyLocalizer) : IBulkModificationLocalizer
{
    public string DefaultName() => localizer[nameof(DefaultName)];

    public string VariableIsNotFound(string variableName) => localizer[nameof(VariableIsNotFound), variableName];

    public string PropertyIsNotFound(PropertyPool? pool, int? id)
    {
        var idOrName = id?.ToString();
        if (id.HasValue && pool is PropertyPool.Internal or PropertyPool.Reserved)
        {
            idOrName = propertyLocalizer.BuiltinPropertyName((ResourceProperty) id.Value);
        }

        return localizer[nameof(PropertyIsNotFound),
            pool.HasValue ? propertyLocalizer.PropertyPoolName(pool.Value) : bakabaseLocalizer.Unknown(),
            idOrName ?? bakabaseLocalizer.Unknown()];
    }
}