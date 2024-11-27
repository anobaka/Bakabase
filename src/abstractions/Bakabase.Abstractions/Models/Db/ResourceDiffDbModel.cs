using Bakabase.Abstractions.Components.Property;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db;

public record ResourceDiffDbModel: IPropertyKeyHolder
{
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public string? Value1 { get; set; }
    public string? Value2 { get; set; }
}