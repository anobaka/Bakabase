using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Enhancer.Models.Domain;

public record EnhancerFullOptions : EnhancerOptions
{
    private Dictionary<int, EnhancerTargetFullOptions>? _targetFullOptionsMap;

    public Dictionary<int, EnhancerTargetFullOptions>? TargetFullOptionsMap
    {
        get => _targetFullOptionsMap;
        set => TargetOptionsMap =
            (_targetFullOptionsMap = value)?.ToDictionary(d => d.Key, d => (EnhancerTargetOptions) d.Value);
    }
}