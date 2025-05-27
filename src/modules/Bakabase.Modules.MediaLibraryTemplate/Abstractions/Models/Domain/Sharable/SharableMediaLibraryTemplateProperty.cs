using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain.Sharable;

public record SharableMediaLibraryTemplateProperty
{
    public Bakabase.Abstractions.Models.Domain.Property Property { get; set; } = null!;
    public List<PathLocator>? ValueLocators { get; set; }
}