using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record ResourceSearchFilter
{
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public SearchOperation Operation { get; set; }
    public object? DbValue { get; set; }
    public Property Property { get; set; } = null!;
}