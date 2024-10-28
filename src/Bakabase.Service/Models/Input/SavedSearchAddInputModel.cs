using Bakabase.Abstractions.Models.Input;

namespace Bakabase.Service.Models.Input;

public record SavedSearchAddInputModel
{
    public ResourceSearchInputModel Search { get; set; } = null!;
    public string Name { get; set; } = null!;
}