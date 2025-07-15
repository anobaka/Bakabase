using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db;

public record MediaLibraryTemplatePropertyDbModel
{
    public PropertyPool Pool { get; set; }
    public int Id { get; set; }
    public List<PathPropertyExtractor>? ValueLocators { get; set; }
}