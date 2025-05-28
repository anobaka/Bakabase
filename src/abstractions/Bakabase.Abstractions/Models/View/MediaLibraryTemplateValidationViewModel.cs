using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Models.View;

public record MediaLibraryTemplateValidationViewModel
{
    public bool Passed => UnhandledExtensionGroups?.Any() != true && UnhandledProperties?.Any() != true;
    public List<Bakabase.Abstractions.Models.Domain.Property>? UnhandledProperties { get; set; }
    public List<ExtensionGroup>? UnhandledExtensionGroups { get; set; }
}