namespace Bakabase.Modules.Enhancer.Abstractions.Components;

public abstract class AbstractEnhancementConverter<TRawValue, TTargetProperty> : IEnhancementConverter
    where TTargetProperty : class
{
    protected abstract TRawValue? Convert(TRawValue? rawValue, TTargetProperty targetProperty);

    public object? Convert(object? rawValue, Bakabase.Abstractions.Models.Domain.CustomProperty targetProperty)
    {
        var typedValue = rawValue is TRawValue t ? t : default;
        if (typedValue != null && targetProperty is TTargetProperty typedTargetProperty)
        {
            return Convert(typedValue, typedTargetProperty);
        }

        return null;
    }
}