using System.ComponentModel.DataAnnotations;

namespace Bakabase.Service.Models.Input;

public enum FileSystemEntryGroupStrategyType
{
    Similarity = 0,
    KeyExtraction = 1,
    Affix = 2,
}

public enum FileSystemEntryGroupAffixDirection
{
    Prefix = 0,
    Suffix = 1,
    Both = 2,
}

public record FileSystemEntryGroupInputModel
{
    public string[] Paths { get; set; } = [];
    public bool GroupInternal { get; set; }
    public FileSystemEntryGroupStrategyType StrategyType { get; set; } =
        FileSystemEntryGroupStrategyType.Similarity;

    [Range(0, 1)]
    public decimal SimilarityThreshold { get; set; } = 1.0m;

    public string? KeyExtractionRegex { get; set; }

    public FileSystemEntryGroupAffixDirection AffixDirection { get; set; } =
        FileSystemEntryGroupAffixDirection.Prefix;

    [Range(1, 1000)]
    public int AffixMinLength { get; set; } = 3;
}
