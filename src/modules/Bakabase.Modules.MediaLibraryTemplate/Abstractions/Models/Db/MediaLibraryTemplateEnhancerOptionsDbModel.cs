using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Db;

public record MediaLibraryTemplateEnhancerOptionsDbModel
{
    public int EnhancerId { get; set; }
    public List<MediaLibraryTemplateEnhancerTargetAllInOneOptionsDbModel>? TargetOptions { get; set; }
}