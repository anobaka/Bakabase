using System.Text.Json;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

/// <summary>
/// Discovers ExHentai galleries as resources.
/// Uses cached gallery data from ExHentaiGalleryService.
/// </summary>
public class ExHentaiResolver : IResourceResolver
{
    private readonly IExHentaiGalleryService _galleryService;
    private readonly ExHentaiClient _exHentaiClient;
    private readonly ILogger<ExHentaiResolver> _logger;

    public ExHentaiResolver(IExHentaiGalleryService galleryService, ExHentaiClient exHentaiClient,
        ILogger<ExHentaiResolver> logger)
    {
        _galleryService = galleryService;
        _exHentaiClient = exHentaiClient;
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

            var coverUrls = !string.IsNullOrEmpty(gallery.CoverUrl)
                ? new List<string> { gallery.CoverUrl }
                : null;

            resources.Add(new ResolvedResource
            {
                SourceKey = $"{gallery.GalleryId}/{gallery.GalleryToken}",
                DisplayName = gallery.Title ?? gallery.TitleJpn ?? $"Gallery {gallery.GalleryId}",
                Path = gallery.IsDownloaded ? gallery.LocalPath : null,
                Source = ResourceSource.ExHentai,
                CoverUrls = coverUrls
            });
        }

        _logger.LogInformation("ExHentai resolver discovered {Count} resources ({Downloaded} downloaded)",
            resources.Count, resources.Count(r => r.Path != null));

        return resources;
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

    public async Task<Dictionary<string, string>> GetDefaultDisplayNames(IEnumerable<string> sourceKeys)
    {
        var result = new Dictionary<string, string>();
        var keySet = sourceKeys.ToHashSet();
        if (keySet.Count == 0) return result;

        var galleries = await _galleryService.GetAll();
        foreach (var gallery in galleries)
        {
            var key = $"{gallery.GalleryId}/{gallery.GalleryToken}";
            if (keySet.Contains(key))
            {
                var name = gallery.Title ?? gallery.TitleJpn ?? $"Gallery {gallery.GalleryId}";
                result[key] = name;
            }
        }

        return result;
    }

    public async Task<List<PlayableItem>> DiscoverPlayableItemsAsync(Resource resource, string sourceKey, CancellationToken ct)
    {
        var galleries = await _galleryService.GetAll();
        var gallery = galleries.FirstOrDefault(g => $"{g.GalleryId}/{g.GalleryToken}" == sourceKey);
        var displayName = gallery?.Title ?? gallery?.TitleJpn ?? $"Gallery {sourceKey}";

        return
        [
            new PlayableItem
            {
                Origin = DataOrigin.ExHentai,
                Key = sourceKey,
                DisplayName = displayName
            }
        ];
    }

    public Task PlayAsync(Resource resource, PlayableItem item, CancellationToken ct)
    {
        // Open ExHentai gallery page
        var url = $"https://exhentai.org/g/{item.Key}/";
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            }
        };
        process.Start();
        return Task.CompletedTask;
    }

    public List<SourceMetadataFieldInfo> GetPredefinedMetadataFields() =>
    [
        new(nameof(ExHentaiMetadataField.Name), StandardValueType.String),
        new(nameof(ExHentaiMetadataField.RawName), StandardValueType.String),
        new(nameof(ExHentaiMetadataField.Introduction), StandardValueType.String),
        new(nameof(ExHentaiMetadataField.Rate), StandardValueType.Decimal),
        new(nameof(ExHentaiMetadataField.Category), StandardValueType.String),
        new(nameof(ExHentaiMetadataField.CoverUrl), StandardValueType.String),
        new(nameof(ExHentaiMetadataField.FileCount), StandardValueType.Decimal),
        new(nameof(ExHentaiMetadataField.PageCount), StandardValueType.Decimal),
    ];

    public async Task<SourceDetailedMetadata?> FetchDetailedMetadataAsync(string sourceKey, CancellationToken ct)
    {
        var url = $"https://exhentai.org/g/{sourceKey}/";
        var detail = await _exHentaiClient.ParseDetail(url, false);
        if (detail == null) return null;

        var result = new SourceDetailedMetadata
        {
            RawJson = JsonSerializer.Serialize(detail, JsonSerializerOptions.Web),
            CoverUrls = !string.IsNullOrEmpty(detail.CoverUrl) ? [detail.CoverUrl] : null,
            PredefinedFieldValues =
            {
                [nameof(ExHentaiMetadataField.Name)] = detail.Name,
                [nameof(ExHentaiMetadataField.RawName)] = detail.RawName,
                [nameof(ExHentaiMetadataField.Introduction)] = detail.Introduction,
                [nameof(ExHentaiMetadataField.Rate)] = detail.Rate != 0 ? detail.Rate : null,
                [nameof(ExHentaiMetadataField.Category)] = detail.Category.ToString(),
                [nameof(ExHentaiMetadataField.CoverUrl)] = detail.CoverUrl,
                [nameof(ExHentaiMetadataField.FileCount)] = (decimal)detail.FileCount,
                [nameof(ExHentaiMetadataField.PageCount)] = (decimal)detail.PageCount,
            }
        };

        // Tags are dynamic fields: each tag group → List<string>
        if (detail.Tags is { Count: > 0 })
        {
            foreach (var (group, tags) in detail.Tags)
            {
                result.CustomFieldValues[group] = tags.ToList();
            }
        }

        return result;
    }
}
