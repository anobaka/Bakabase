using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record Property(
    ResourcePropertyType Type,
    int Id,
    StandardValueType DbValueType,
    StandardValueType BizValueType,
    int CustomPropertyType = 0,
    string? CustomPropertyName = null,
    object? Options = null)
{
    public ResourcePropertyType Type { get; set; } = Type;
    public int Id { get; set; } = Id;

    /// <summary>
    /// Do not use this property if <see cref="Type"/> is <see cref="ResourcePropertyType.Custom"/>
    /// </summary>
    public ResourceProperty EnumId => (ResourceProperty) Id;

    public StandardValueType DbValueType { get; set; } = DbValueType;
    public StandardValueType BizValueType { get; set; } = BizValueType;

    public string? CustomPropertyName { get; set; } = CustomPropertyName;

    /// <summary>
    /// Only available when <see cref="Type"/> equals <see cref="ResourcePropertyType.Custom"/>
    /// </summary>
    public int CustomPropertyType { get; set; } = CustomPropertyType;

    /// <summary>
    /// Only available when <see cref="Type"/> equals <see cref="ResourcePropertyType.Custom"/>
    /// </summary>
    public object? Options { get; set; } = Options;
}