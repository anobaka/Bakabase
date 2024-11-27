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
        public int? Position { get; set; }
        public bool? Reverse { get; set; }
        public bool? RemoveBefore { get; set; }
        public int? Count { get; set; }
        public int? Start { get; set; }
        public int? End { get; set; }
    }
}