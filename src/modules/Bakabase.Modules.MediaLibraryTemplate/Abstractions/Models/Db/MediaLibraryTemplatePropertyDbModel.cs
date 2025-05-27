using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Db;

public record MediaLibraryTemplatePropertyDbModel
{
    public PropertyPool Pool { get; set; }
    public int Id { get; set; }
    public List<PathLocator>? ValueLocators { get; set; }
}