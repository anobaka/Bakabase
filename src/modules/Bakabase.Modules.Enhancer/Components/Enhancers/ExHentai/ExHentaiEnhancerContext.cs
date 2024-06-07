namespace Bakabase.Modules.Enhancer.Components.Enhancers.ExHentai
{
    public record ExHentaiEnhancerContext
    {
        public decimal? Rating { get; set; }
        public Dictionary<string, List<string>>? Tags { get; set; }
    }
}
