namespace Bakabase.Abstractions.Models.Db;

public record MediaLibraryTemplateEnhancerOptionsDbModel
{
    public int EnhancerId { get; set; }
    public List<MediaLibraryTemplateEnhancerTargetAllInOneOptionsDbModel>? TargetOptions { get; set; }

    /// <summary>
    /// For regex enhancer
    /// </summary>
    public string? Expressions { get; set; }
}