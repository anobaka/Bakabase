using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Enhancer.Models.Domain;

public record CategoryEnhancerFullOptions : CategoryEnhancerOptions
{
    private EnhancerFullOptions? _fullOptions;

    public EnhancerFullOptions? FullOptions
    {
        get => _fullOptions;
        set => Options = _fullOptions = value;
    }
}