using Bakabase.Abstractions.Components.Property;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;

public record MediaLibraryTemplateProperty
{
    public Bakabase.Abstractions.Models.Domain.Property Property { get; set; } = null!;
    public List<PathLocator>? ValueLocators { get; set; }
}