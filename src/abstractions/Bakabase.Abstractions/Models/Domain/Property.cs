using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record Property(
    PropertyPool Pool,
    int Id,
    PropertyType Type,
    string? Name = null,
    object? Options = null)
{
    public PropertyPool Pool { get; set; } = Pool;
    public int Id { get; set; } = Id;
    public string Name { get; set; } = Name ?? Type.ToString();
    public PropertyType Type { get; set; } = Type;
    public object? Options { get; set; } = Options;
}