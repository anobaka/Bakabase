using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record PropertyKey(PropertyPool Pool, int Id)
{
    public PropertyPool Pool { get; set; } = Pool;
    public int Id { get; set; } = Id;
}