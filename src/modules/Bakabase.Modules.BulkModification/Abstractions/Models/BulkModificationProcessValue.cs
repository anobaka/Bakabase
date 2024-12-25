using Bakabase.Abstractions.Components.Property;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.Modules.BulkModification.Abstractions.Models;

public record BulkModificationProcessValue
{
    public BulkModificationProcessorValueType Type { get; set; }
    public PropertyPool? PropertyPool { get; set; }
    public int? PropertyId { get; set; }
    public PropertyType? PropertyType { get; set; }

    /// <summary>
    /// Static Text / Variable Key / Serialized BizValue / Serialized DbValue
    /// </summary>
    public string? Value { get; set; }

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
                    value = Value.DeserializeAsStandardValue(toValueType);
                    fromValueType = toValueType;
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
                case BulkModificationProcessorValueType.DynamicPropertyDbValue:
                case BulkModificationProcessorValueType.DynamicPropertyBizValue:
                {
                    if (PropertyPool.HasValue && PropertyId.HasValue)
                    {
                        var isDbValue = Type == BulkModificationProcessorValueType.DynamicPropertyDbValue;
                        var property = propertyMap?.GetValueOrDefault(PropertyPool.Value)
                            ?.GetValueOrDefault(PropertyId.Value);
                        if (property != null)
                        {
                            value = Value.DeserializeAsStandardValue(isDbValue
                                ? property.Type.GetDbValueType()
                                : property.Type.GetBizValueType());
                            if (isDbValue)
                            {
                                value = property.GetBizValue(value);
                            }

                            fromValueType = property.Type.GetBizValueType();
                        }
                    }

                    throw new Exception(localizer.PropertyIsNotFound(PropertyPool, PropertyId));
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