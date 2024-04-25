namespace Bakabase.Modules.Enhancer.Enhancers.ExHentai
{
    public record ExHentaiEnhancerContext
    {
        public decimal? Rating { get; set; }
        public Dictionary<string, List<string>>? Tags { get; set; }
    }
}
