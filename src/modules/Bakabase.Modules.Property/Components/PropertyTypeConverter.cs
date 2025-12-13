using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.StandardValue;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.Property.Components;

/// <summary>
/// Default implementation of property type converter.
/// Converts property values between different property types using StandardValue conversion.
/// </summary>
public class PropertyTypeConverter : IPropertyTypeConverter
{
    private readonly IStandardValueService _standardValueService;

    public PropertyTypeConverter(IStandardValueService standardValueService)
    {
        _standardValueService = standardValueService;
    }

    public async Task<PropertyValueConversionResult> ConvertValueAsync(
        Bakabase.Abstractions.Models.Domain.Property fromProperty,
        Bakabase.Abstractions.Models.Domain.Property toProperty,
        object? dbValue)
    {
        var fromDescriptor = PropertySystem.Property.GetDescriptor(fromProperty.Type);
        var toDescriptor = PropertySystem.Property.GetDescriptor(toProperty.Type);

        // 1. DbValue -> BizValue
        var bizValue = fromDescriptor.GetBizValue(fromProperty, dbValue);

        // 2. BizValue -> NewBizValue (via StandardValue conversion)
        var newBizValue = await _standardValueService.Convert(
            bizValue,
            PropertySystem.Property.GetBizValueType(fromProperty.Type),
            PropertySystem.Property.GetBizValueType(toProperty.Type));

        // 3. NewBizValue -> NewDbValue
        var (newDbValue, propertyChanged) = toDescriptor.PrepareDbValue(toProperty, newBizValue);

        return new PropertyValueConversionResult(newDbValue, propertyChanged, toProperty);
    }

    public async Task<PropertyTypeConversionPreview> PreviewConversionAsync(
        Bakabase.Abstractions.Models.Domain.Property fromProperty,
        PropertyType toType,
        IEnumerable<object?> dbValues)
    {
        var fromDescriptor = PropertySystem.Property.GetDescriptor(fromProperty.Type);
        var fromBizType = PropertySystem.Property.GetBizValueType(fromProperty.Type);
        var toBizType = PropertySystem.Property.GetBizValueType(toType);

        var fromHandler = StandardValueSystem.GetHandler(fromBizType);
        var toHandler = StandardValueSystem.GetHandler(toBizType);

        var changes = new List<PropertyValueChangePreview>();
        var total = 0;

        foreach (var dbValue in dbValues)
        {
            total++;

            // Convert to biz value
            var bizValue = fromDescriptor.GetBizValue(fromProperty, dbValue);
            var newBizValue = await _standardValueService.Convert(bizValue, fromBizType, toBizType);

            // Build display values
            var fromDisplay = fromHandler.BuildDisplayValue(bizValue);
            var toDisplay = toHandler.BuildDisplayValue(newBizValue);

            // Only record if values are different
            if (fromDisplay != toDisplay)
            {
                var fromSerialized = bizValue?.SerializeAsStandardValue(fromBizType);
                var toSerialized = newBizValue?.SerializeAsStandardValue(toBizType);

                changes.Add(new PropertyValueChangePreview(fromDisplay, toDisplay, fromSerialized, toSerialized));
            }
        }

        return new PropertyTypeConversionPreview(total, fromBizType, toBizType, changes);
    }

    public async Task<PropertyBatchConversionResult> ConvertValuesAsync(
        Bakabase.Abstractions.Models.Domain.Property fromProperty,
        Bakabase.Abstractions.Models.Domain.Property toProperty,
        IEnumerable<object?> dbValues)
    {
        var fromDescriptor = PropertySystem.Property.GetDescriptor(fromProperty.Type);
        var toDescriptor = PropertySystem.Property.GetDescriptor(toProperty.Type);
        var fromBizType = PropertySystem.Property.GetBizValueType(fromProperty.Type);
        var toBizType = PropertySystem.Property.GetBizValueType(toProperty.Type);

        var newDbValues = new List<object?>();
        var anyPropertyChanged = false;

        foreach (var dbValue in dbValues)
        {
            // 1. DbValue -> BizValue
            var bizValue = fromDescriptor.GetBizValue(fromProperty, dbValue);

            // 2. BizValue -> NewBizValue
            var newBizValue = await _standardValueService.Convert(bizValue, fromBizType, toBizType);

            // 3. NewBizValue -> NewDbValue
            var (newDbValue, propertyChanged) = toDescriptor.PrepareDbValue(toProperty, newBizValue);

            newDbValues.Add(newDbValue);
            anyPropertyChanged = anyPropertyChanged || propertyChanged;
        }

        return new PropertyBatchConversionResult(newDbValues, anyPropertyChanged, toProperty);
    }
}
