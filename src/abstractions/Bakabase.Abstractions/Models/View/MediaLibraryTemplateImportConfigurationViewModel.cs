using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Models.View;

public record MediaLibraryTemplateImportConfigurationViewModel
{
    public bool NoNeedToConfigure => UniqueCustomProperties?.Any() != true && UniqueExtensionGroups?.Any() != true;
    public List<Property>? UniqueCustomProperties { get; set; }
    public List<ExtensionGroup>? UniqueExtensionGroups { get; set; }
}