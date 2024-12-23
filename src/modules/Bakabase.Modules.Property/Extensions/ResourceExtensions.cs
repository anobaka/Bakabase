using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Extensions;

public static class ResourceExtensions
{
    /// <summary>
    /// todo: don't know where to put this method
    /// </summary>
    /// <param name="properties"></param>
    /// <param name="property"></param>
    /// <param name="value"></param>
    /// <param name="scope"></param>
    public static void SetPropertyBizValue(this Dictionary<int, Dictionary<int, Resource.Property>> properties,
        Bakabase.Abstractions.Models.Domain.Property property, object? value, PropertyValueScope scope)
    {
        if (!properties.TryGetValue((int) property.Pool, out var pvs))
        {
            pvs = properties[(int) property.Pool] = [];
        }

        if (!pvs.TryGetValue(property.Id, out var pv))
        {
            pv = pvs[property.Id] = new Resource.Property(property.Name, property.Type, property.Type.GetDbValueType(),
                property.Type.GetBizValueType(), null);
        }

        pv.Values ??= [];
        var mv = pv.Values.FirstOrDefault(x => x.Scope == (int) scope);
        if (mv == null)
        {
            mv = new Resource.Property.PropertyValue((int) scope, null, value, value);
            pv.Values.Add(mv);
        }
        else
        {
            mv.BizValue = value;
        }
    }

    /// <summary>
    /// todo: don't know where to put this method
    /// </summary>
    /// <param name="properties"></param>
    /// <param name="propertyPool"></param>
    /// <param name="propertyId"></param>
    /// <param name="scope"></param>
    /// <returns></returns>
    public static (object? Value, StandardValueType DbValueType, StandardValueType BizValueType)?
        GetPropertyBizValue(this Dictionary<int, Dictionary<int, Resource.Property>> properties,
            PropertyPool propertyPool, int propertyId, PropertyValueScope scope)
    {
        var pvs = properties?.GetValueOrDefault((int) propertyPool)?.GetValueOrDefault(propertyId);
        if (pvs == null)
        {
            return null;
        }

        var v = pvs.Values?.FirstOrDefault(x => x.Scope == (int) PropertyValueScope.Manual);

        return (v?.BizValue, pvs.DbValueType, pvs.BizValueType);
    }
}