namespace Bakabase.Abstractions.Models.Domain;

/// <summary>Trusted configuration stored on the viewing device, after source-owned matching.</summary>
public sealed record ResourceProfileOpeningOptions
{
    public int[] ProfileIds { get; init; } = [];
    public string? OpeningMode { get; init; }
    public ResourceProfilePlayerOptions? PlayerOptions { get; init; }
}
