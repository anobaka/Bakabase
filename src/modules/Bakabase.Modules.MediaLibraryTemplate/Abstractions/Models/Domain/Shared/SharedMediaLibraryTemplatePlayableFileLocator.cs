using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain.Shared;

public record SharedMediaLibraryTemplatePlayableFileLocator
{
    public ExtensionGroup[]? ExtensionGroups { get; set; }
    public HashSet<string>? Extensions { get; set; }
}