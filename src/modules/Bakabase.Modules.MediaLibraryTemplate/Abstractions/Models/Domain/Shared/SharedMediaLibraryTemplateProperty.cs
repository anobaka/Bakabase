using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathLocator;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain.Shared;

public record SharedMediaLibraryTemplateProperty
{
    public Bakabase.Abstractions.Models.Domain.Property Property { get; set; } = null!;
    public List<PathLocator>? ValueLocators { get; set; }
}