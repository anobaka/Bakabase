using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record Property(
    PropertyPool Pool,
    int Id,
    PropertyType Type,
    string? Name = null,
    object? Options = null,
    int Order = int.MaxValue): PropertyKey(Pool, Id)
{
    public string Name { get; set; } = Name ?? Type.ToString();
    public PropertyType Type { get; set; } = Type;
    public object? Options { get; set; } = Options;
    public int Order { get; set; } = Order;
}