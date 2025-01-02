using Bakabase.Abstractions.Components.Property;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.Modules.BulkModification.Abstractions.Models;

public record BulkModificationProcessValue
{
    public BulkModificationProcessorValueType Type { get; set; }
    public PropertyPool? PropertyPool { get; set; }
    public int? PropertyId { get; set; }
    public PropertyType? EditorPropertyType { get; set; }

    /// <summary>
    /// Static Text / Variable Key / Serialized BizValue / Serialized DbValue
    /// </summary>
    public string? Value { get; set; }

    public bool FollowPropertyChanges { get; set; } = true;

    public Bakabase.Abstractions.Models.Domain.Property? Property { get; set; }

    public void PopulateData(PropertyMap? propertyMap)
    {
        if (EditorPropertyType.HasValue)
        {
            if (EditorPropertyType.Value.IsReferenceValueType())
            {
                if (PropertyPool.HasValue && PropertyId.HasValue)
                {
                    Property = propertyMap?.GetProperty(PropertyPool.Value, PropertyId.Value);
                }
            }
            else
            {
                Property = PropertyInternals.VirtualPropertyMap.GetValueOrDefault(EditorPropertyType.Value);
            }
        }
    }

    public TValue? ConvertToStdValue<TValue>(StandardValueType toValueType,
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Dictionary<PropertyPool, Dictionary<int, Bakabase.Abstractions.Models.Domain.Property>>? propertyMap,
        IBulkModificationLocalizer localizer)
    {
        if (Value.IsNotEmpty())
        {
            object? value;
            StandardValueType fromValueType;
            switch (Type)
            {
                case BulkModificationProcessorValueType.ManuallyInput:
                {
                    if (!EditorPropertyType.HasValue)
                    {
                        throw new Exception(localizer.EditorPropertyTypeIsNotSet());
                    }

                    if (FollowPropertyChanges)
                    {
                        if (PropertyPool.HasValue && PropertyId.HasValue)
                        {
                            var property = propertyMap?.GetValueOrDefault(PropertyPool.Value)
                                ?.GetValueOrDefault(PropertyId.Value);
                            if (property != null)
                            {
                                value = Value.DeserializeAsStandardValue(EditorPropertyType.Value.GetDbValueType());
                                value = property.GetBizValue(value);
                            }
                            else
                            {
                                throw new Exception(localizer.PropertyIsNotFound(PropertyPool, PropertyId));
                            }
                        }
                        else
                        {
                            throw new Exception(localizer.PropertyIsNotFound(PropertyPool, PropertyId));
                        }
                    }
                    else
                    {
                        value = Value.DeserializeAsStandardValue(EditorPropertyType.Value.GetBizValueType());
                    }

                    fromValueType = EditorPropertyType.Value.GetBizValueType();
                    break;
                }
                case BulkModificationProcessorValueType.Variable:
                {
                    if (variableMap?.TryGetValue(Value, out var tav) == true)
                    {
                        value = tav.Value;
                        fromValueType = tav.Type;
                    }
                    else
                    {
                        throw new Exception(localizer.VariableIsNotFound(Value));
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var stdHandler = fromValueType.GetHandler();
            if (stdHandler.Convert(value, toValueType) is TValue tv)
            {
                return tv;
            }
        }

        return default;
    }
}