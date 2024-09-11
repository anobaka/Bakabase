using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Enhancer.Abstractions.Components;

public interface IEnhancementConverter
{
    object? Convert(object? rawValue, Property property);
}