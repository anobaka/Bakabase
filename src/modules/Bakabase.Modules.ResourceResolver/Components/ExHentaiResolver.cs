using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.ResourceResolver.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ResourceResolver.Components;

/// <summary>
/// Discovers ExHentai galleries as resources.
/// Uses cached gallery data from ExHentaiGalleryService.
/// Metadata retrieval is handled by the existing ExHentaiEnhancer.
/// </summary>
public class ExHentaiResolver : IResourceResolver
{
    private readonly IExHentaiGalleryService _galleryService;
    private readonly ILogger<ExHentaiResolver> _logger;

    public ExHentaiResolver(IExHentaiGalleryService galleryService, ILogger<ExHentaiResolver> logger)
    {
        _galleryService = galleryService;
        _logger = logger;
    }

    public ResourceSource Source => ResourceSource.ExHentai;

    public async Task<List<ResolvedResource>> DiscoverResources(CancellationToken ct)
    {
        var galleries = await _galleryService.GetAll();
        var resources = new List<ResolvedResource>();

        foreach (var gallery in galleries)
        {
            ct.ThrowIfCancellationRequested();

            resources.Add(new ResolvedResource
            {
                SourceKey = $"{gallery.GalleryId}/{gallery.GalleryToken}",
                DisplayName = gallery.Title ?? gallery.TitleJpn ?? $"Gallery {gallery.GalleryId}",
                Path = gallery.IsDownloaded ? gallery.LocalPath : null,
                Source = ResourceSource.ExHentai
            });
        }

        _logger.LogInformation("ExHentai resolver discovered {Count} resources ({Downloaded} downloaded)",
            resources.Count, resources.Count(r => r.Path != null));

        return resources;
    }

    public Task<ResolvedCover?> GetCover(Resource resource, CancellationToken ct)
    {
        // Cover is handled by ExHentaiEnhancer
        return Task.FromResult<ResolvedCover?>(null);
    }

    public ResolverConfigurationSchema GetConfigurationSchema()
    {
        return new ResolverConfigurationSchema
        {
            Fields =
            [
                new ResolverConfigField
                {
                    Key = "cookie",
                    Label = "ExHentai Cookie",
                    Description = "Cookie for accessing ExHentai favorites",
                    Type = ResolverConfigFieldType.Password,
                    Required = true
                }
            ]
        };
    }

    public ResolverPlayerConfig? GetDefaultPlayerConfig()
    {
        // Use system image viewer
        return null;
    }

    public IPlayableFileSelector? GetPlayableFileSelector()
    {
        // Uses ImagePlayableFileSelector
        return null;
    }

    public Task<List<MigrationCandidate>> IdentifyMigrationCandidates(
        List<Resource> fileSystemResources, CancellationToken ct)
    {
        // Not implementing migration from FileSystem for ExHentai (per plan)
        return Task.FromResult(new List<MigrationCandidate>());
    }

    public Task MigrateResources(List<MigrationCandidate> candidates, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
