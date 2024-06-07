namespace Bakabase.Modules.Enhancer.Components.Enhancers.Bangumi
{
    public record BangumiEnhancerContext
    {
        public string? Name { get; set; }
        public decimal? Rating { get; set; }
        public List<string>? Tags { get; set; }
        public List<string>? Originals { get; set; }
    }
}
