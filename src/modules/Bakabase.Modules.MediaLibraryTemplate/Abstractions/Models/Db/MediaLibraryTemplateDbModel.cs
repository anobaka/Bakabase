using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathFilter;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Db;

public record MediaLibraryTemplateDbModel(
    int Id,
    string Name,
    string? ResourceFilters = null,
    string? Properties = null,
    string? PlayableFileLocator = null,
    string? Enhancers = null,
    string? DisplayNameTemplate = null,
    string? SamplePaths = null
);
