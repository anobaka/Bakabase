using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.StandardValue;

public class StandardValueAttribute(StandardValueType valueType) : Attribute
{
    public StandardValueType ValueType { get; } = valueType;
}