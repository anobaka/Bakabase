using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Enhancer.Abstractions.Models.Domain;

/// <summary>
/// To be simple, we put all possible options into one model for now, even they may be not suitable for current enhancer.
/// </summary>
public record EnhancerFullOptions
{
    public List<EnhancerTargetFullOptions>? TargetOptions { get; set; }
    public List<string>? Expressions { get; set; }
}