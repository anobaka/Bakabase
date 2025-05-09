using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models;

public record MediaLibraryTemplatePlayableFileLocator
{
    public HashSet<FileExtensionGroup>? ExtensionGroups { get; set; }
    public HashSet<string>? Extensions { get; set; }
}