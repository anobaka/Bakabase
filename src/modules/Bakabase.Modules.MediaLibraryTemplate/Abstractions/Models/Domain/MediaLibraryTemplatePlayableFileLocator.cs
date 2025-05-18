using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;

public record MediaLibraryTemplatePlayableFileLocator
{
    public HashSet<int>? ExtensionGroupIds { get; set; }
    public ExtensionGroup[]? ExtensionGroups { get; set; }
    public HashSet<string>? Extensions { get; set; }
}