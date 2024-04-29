namespace Bakabase.Abstractions.Models.Domain;

public record EnhancerOptions()
{
    public Dictionary<int, EnhancerTargetOptions>? TargetOptionsMap { get; set; }
}