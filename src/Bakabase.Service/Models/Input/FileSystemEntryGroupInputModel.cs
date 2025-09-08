using System.ComponentModel.DataAnnotations;

namespace Bakabase.Service.Models.Input;

public record FileSystemEntryGroupInputModel
{
    public string[] Paths { get; set; } = [];
    public bool GroupInternal { get; set; }
    [Range(0, 1)]
    public decimal SimilarityThreshold { get; set; } = 1.0m;
}