namespace Bakabase.Abstractions.Models.Db;

public record MediaLibraryTemplateEnhancerOptionsDbModel
{
    public int EnhancerId { get; set; }
    public List<MediaLibraryTemplateEnhancerTargetAllInOneOptionsDbModel>? TargetOptions { get; set; }
}