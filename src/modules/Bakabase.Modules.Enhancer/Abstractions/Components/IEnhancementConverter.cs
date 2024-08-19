namespace Bakabase.Modules.Enhancer.Abstractions.Components;

public interface IEnhancementConverter
{
    object? Convert(object? rawValue, Bakabase.Abstractions.Models.Domain.CustomProperty targetProperty);
}