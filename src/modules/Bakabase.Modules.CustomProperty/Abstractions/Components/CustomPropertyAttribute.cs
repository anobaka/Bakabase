using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Abstractions.Components;

[AttributeUsage(AttributeTargets.Field)]
public class CustomPropertyAttribute(StandardValueType dbValueType, StandardValueType bizValueType)
    : Attribute
{
    public StandardValueType DbValueType { get; } = dbValueType;
    public StandardValueType BizValueType { get; } = bizValueType;
}