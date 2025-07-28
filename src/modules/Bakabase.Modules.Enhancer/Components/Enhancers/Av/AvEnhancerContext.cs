using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Av;

public class AvEnhancerContext
{
    public List<IAvDetail> Details { get; set; } = [];
    public Dictionary<string, string> CoverPaths { get; set; } = new();
    public Dictionary<string, string> PosterPaths { get; set; } = new();
}