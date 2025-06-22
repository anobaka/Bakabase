using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Components;

[AttributeUsage(AttributeTargets.Field)]
public class PresetPropertyAttribute(PropertyType type) : Attribute
{
    public PropertyType Type { get; } = type;
}