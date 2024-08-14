namespace Bakabase.Modules.Enhancer.Components.Enhancers.ExHentai
{
    public record ExHentaiEnhancerContext
    {
        public string? Name { get; set; }
        public decimal? Rating { get; set; }
        /// <summary>
        /// Group - Tags
        /// </summary>
        public Dictionary<string, List<string>>? Tags { get; set; }
        public string? Introduction { get; set; }
        public string? CoverPath { get; set; }
    }
}
