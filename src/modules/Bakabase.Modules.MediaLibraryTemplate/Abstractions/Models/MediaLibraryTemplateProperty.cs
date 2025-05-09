using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathLocator;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models;

public record MediaLibraryTemplateProperty
{
    public PropertyPool Pool { get; set; }
    public int Id { get; set; }

    public Bakabase.Abstractions.Models.Domain.Property? Property { get; set; }
    public List<PathLocator>? ValueLocators { get; set; }
}