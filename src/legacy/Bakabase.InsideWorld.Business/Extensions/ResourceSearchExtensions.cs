using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;

namespace Bakabase.InsideWorld.Business.Extensions;

public static class ResourceSearchExtensions
{
    public static bool IsValid(this ResourceSearchFilter filter)
    {
        if (filter is { PropertyPool: PropertyPool.Custom, Property: null })
        {
            return false;
        }

        var propertyType = filter.PropertyPool == PropertyPool.Custom
            ? filter.Property!.Type
            : PropertyInternals.BuiltinPropertyMap.GetValueOrDefault((ResourceProperty)filter.PropertyId)?.Type;

        if (!propertyType.HasValue)
        {
            return false;
        }

        var psh = PropertyInternals.PropertySearchHandlerMap.GetValueOrDefault(propertyType.Value);
        if (psh == null || psh.SearchOperations.TryGetValue(filter.Operation, out var psoo) != true)
        {
            return false;
        }

        if (filter.Operation is not (SearchOperation.IsNull or SearchOperation.IsNotNull))
        {
            var dbValueType = psoo?.AsType.GetDbValueType();
            if (!dbValueType.HasValue)
            {
                return false;
            }

            var stdValueHandler = StandardValueInternals.HandlerMap.GetValueOrDefault(dbValueType.Value);
            if (stdValueHandler == null)
            {
                return false;
            }

            var optimizedDbValue = stdValueHandler.Optimize(filter.DbValue);

            if (filter.Operation is not SearchOperation.IsNull and not SearchOperation.IsNotNull &&
                optimizedDbValue == null)
            {
                return false;
            }
        }

        return true;
    }
}