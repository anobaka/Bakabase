using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Service.Models.Input;

public record ResourceProfileInputModel
{
    public string Name { get; set; } = null!;

    /// <summary>
    /// Search criteria using serialized DbValue format
    /// </summary>
    public ResourceSearchInputModel? Search { get; set; }

    public string? NameTemplate { get; set; }

    public ResourceProfileEnhancerOptions? EnhancerOptions { get; set; }

    public ResourceProfilePlayableFileOptions? PlayableFileOptions { get; set; }

    public ResourceProfilePlayerOptions? PlayerOptions { get; set; }

    public ResourceProfilePropertyOptions? PropertyOptions { get; set; }

    public int Priority { get; set; }
}
