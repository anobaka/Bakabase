using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Models.Db;

public record MediaLibraryTemplateEnhancerOptionsDbModel
{
    public int EnhancerId { get; set; }
    public List<MediaLibraryTemplateEnhancerTargetAllInOneOptionsDbModel>? TargetOptions { get; set; }

    /// <summary>
    /// For regex enhancer
    /// </summary>
    public string? Expressions { get; set; }

    public List<int>? Requirements { get; set; }

    public ScopePropertyKey? KeywordProperty { get; set; }
    public bool? PretreatKeyword { get; set; }
}