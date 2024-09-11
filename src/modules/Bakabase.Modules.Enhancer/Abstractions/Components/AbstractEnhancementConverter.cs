
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Enhancer.Abstractions.Components;

public abstract class AbstractEnhancementConverter<TRawValue> : IEnhancementConverter
{
    protected abstract TRawValue? Convert(TRawValue? rawValue, Property property);

    public object? Convert(object? rawValue, Property property)
    {
        var typedValue = rawValue is TRawValue t ? t : default;
        if (typedValue != null)
        {
            return Convert(typedValue, property);
        }

        return null;
    }
}