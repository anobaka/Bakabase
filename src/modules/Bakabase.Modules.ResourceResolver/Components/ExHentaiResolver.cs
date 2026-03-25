using System.Text.Json;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.ResourceResolver.Abstractions;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ResourceResolver.Components;

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
                Source = ResourceSource.ExHentai,
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
        new("Name", StandardValueType.String),
        new("RawName", StandardValueType.String),
        new("Introduction", StandardValueType.String),
        new("Rate", StandardValueType.Decimal),
        new("Category", StandardValueType.String),
        new("CoverUrl", StandardValueType.String),
        new("FileCount", StandardValueType.Decimal),
        new("PageCount", StandardValueType.Decimal),
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
                ["Name"] = detail.Name,
                ["RawName"] = detail.RawName,
                ["Introduction"] = detail.Introduction,
                ["Rate"] = detail.Rate != 0 ? detail.Rate : null,
                ["Category"] = detail.Category.ToString(),
                ["CoverUrl"] = detail.CoverUrl,
                ["FileCount"] = (decimal)detail.FileCount,
                ["PageCount"] = (decimal)detail.PageCount,
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
