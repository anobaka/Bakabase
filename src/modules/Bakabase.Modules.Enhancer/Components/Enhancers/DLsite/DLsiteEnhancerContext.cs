namespace Bakabase.Modules.Enhancer.Components.Enhancers.DLsite;

public class DLsiteEnhancerContext
{
    public string[]? CoverPaths { get; set; }
    public string? Introduction { get; set; }
    public string? Name { get; set; }
    public decimal? Rating { get; set; }
    public Dictionary<string, List<string>>? PropertiesOnTheRightSideOfCover { get; set; }
}