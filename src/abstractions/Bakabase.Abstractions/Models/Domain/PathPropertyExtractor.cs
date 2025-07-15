using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;


public record PathPropertyExtractor
{
    public PathPropertyExtractorBasePathType BasePathType { get; set; } = PathPropertyExtractorBasePathType.MediaLibrary;
    public PathPositioner Positioner { get; set; }
    public int? Layer { get; set; }
    public string? Regex { get; set; }
}
