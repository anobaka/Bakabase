using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathFilter;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Db;

public record MediaLibraryTemplateDbModel(int Id, string Name, string? ResourceFilters, string? Properties, string? PlayableFileLocator, string? Enhancers, string? DisplayNameTemplate, string? SamplePaths);