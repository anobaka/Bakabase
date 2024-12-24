namespace Bakabase.Abstractions.Components.TextProcessing
{
    /// <summary>
    /// 
    /// </summary>
    public record TextProcessingOptions
    {
        public string? Value { get; set; }
        public string? Find { get; set; }
        public string? Replace { get; set; }
        public int? Index { get; set; }
        public bool? IsPositioningDirectionReversed{ get; set; }
        public bool? IsOperationDirectionReversed { get; set; }
        public int? Count { get; set; }
    }
}