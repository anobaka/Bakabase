using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Enhancer.Models.Domain;

public record EnhancerFullOptions
{
    public Dictionary<int, EnhancerTargetFullOptions>? TargetOptionsMap { get; set; }
}