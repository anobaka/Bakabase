using Bakabase.Abstractions.Models.Input;

namespace Bakabase.Service.Models.Input;

public record SavedSearchAddInputModel
{
    // public string Id { get; set; } = null!;
    public ResourceSearchInputModel Search { get; set; } = null!;
}