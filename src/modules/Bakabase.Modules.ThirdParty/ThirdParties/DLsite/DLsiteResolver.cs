using System.Text.Json;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.DLsite;

/// <summary>
/// Discovers DLsite works as resources.
/// Uses cached DLsiteWork data from DLsiteWorkService.
/// </summary>
public class DLsiteResolver : IResourceResolver
{
    private readonly IDLsiteWorkService _workService;
    private readonly DLsiteClient _dlsiteClient;
    private readonly ILogger<DLsiteResolver> _logger;
    private static readonly Regex WorkIdPattern = new(@"[RBV]J\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DLsiteResolver(IDLsiteWorkService workService, DLsiteClient dlsiteClient, ILogger<DLsiteResolver> logger)
    {
        _workService = workService;
        _dlsiteClient = dlsiteClient;
        _logger = logger;
    }

    public ResourceSource Source => ResourceSource.DLsite;

    public async Task<List<ResolvedResource>> DiscoverResources(CancellationToken ct)
    {
        var works = await _workService.GetAll();
        var resources = new List<ResolvedResource>();

        foreach (var work in works)
        {
            ct.ThrowIfCancellationRequested();

            var coverUrls = !string.IsNullOrEmpty(work.CoverUrl)
                ? new List<string> { work.CoverUrl }
                : null;

            resources.Add(new ResolvedResource
            {
                SourceKey = work.WorkId,
                DisplayName = work.Title ?? work.WorkId,
                Path = work.IsDownloaded ? work.LocalPath : null,
                Source = ResourceSource.DLsite,
                CoverUrls = coverUrls
            });
        }

        _logger.LogInformation("DLsite resolver discovered {Count} resources ({Downloaded} downloaded)",
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
                    Label = "DLsite Cookie",
                    Description = "Cookie for accessing purchased works list",
                    Type = ResolverConfigFieldType.Password,
                    Required = false
                },
                new ResolverConfigField
                {
                    Key = "downloadDirectories",
                    Label = "Download Directories",
                    Description = "Directories to scan for locally downloaded DLsite works",
                    Type = ResolverConfigFieldType.DirectoryList,
                    Required = false
                }
            ]
        };
    }

    public ResolverPlayerConfig? GetDefaultPlayerConfig()
    {
        return new ResolverPlayerConfig
        {
            UseLocaleEmulator = false
        };
    }

    public IPlayableFileSelector? GetPlayableFileSelector()
    {
        // TODO: Implement per-WorkType file selectors
        return null;
    }

    public Task<List<MigrationCandidate>> IdentifyMigrationCandidates(
        List<Resource> fileSystemResources, CancellationToken ct)
    {
        var candidates = new List<MigrationCandidate>();

        foreach (var resource in fileSystemResources)
        {
            if (string.IsNullOrEmpty(resource.Path)) continue;

            // Check filename and path for DLsite Work ID pattern (RJ/BJ/VJ + digits)
            var fileName = resource.FileName ?? "";
            var match = WorkIdPattern.Match(fileName);
            if (!match.Success)
            {
                match = WorkIdPattern.Match(resource.Path);
            }

            if (match.Success)
            {
                candidates.Add(new MigrationCandidate
                {
                    OriginalResource = resource,
                    SourceKey = match.Value.ToUpperInvariant(),
                    Confidence = 0.8f
                });
            }
        }

        return Task.FromResult(candidates);
    }

    public Task MigrateResources(List<MigrationCandidate> candidates, CancellationToken ct)
    {
        _logger.LogInformation("Migrating {Count} resources to DLsite source", candidates.Count);
        return Task.CompletedTask;
    }

    public async Task<Dictionary<string, string>> GetDefaultDisplayNames(IEnumerable<string> sourceKeys)
    {
        var result = new Dictionary<string, string>();
        var keys = sourceKeys.ToList();
        if (keys.Count == 0) return result;

        var works = await _workService.GetByWorkIds(keys);
        foreach (var work in works)
        {
            var name = work.Title ?? work.WorkId;
            result[work.WorkId] = name;
        }

        return result;
    }

    public async Task<List<PlayableItem>> DiscoverPlayableItemsAsync(Resource resource, string sourceKey, CancellationToken ct)
    {
        var works = await _workService.GetByWorkIds([sourceKey]);
        var work = works.FirstOrDefault();
        var displayName = work?.Title ?? sourceKey;

        // DLsite works can be opened via their product page
        return
        [
            new PlayableItem
            {
                Origin = DataOrigin.DLsite,
                Key = sourceKey,
                DisplayName = displayName
            }
        ];
    }

    public Task PlayAsync(Resource resource, PlayableItem item, CancellationToken ct)
    {
        // Open DLsite product page
        var url = $"https://www.dlsite.com/maniax/work/=/product_id/{item.Key}.html";
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
        new(nameof(DLsiteMetadataField.Introduction), StandardValueType.String),
        new(nameof(DLsiteMetadataField.Rating), StandardValueType.Decimal),
        new(nameof(DLsiteMetadataField.CoverUrls), StandardValueType.ListString),
    ];

    public async Task<SourceDetailedMetadata?> FetchDetailedMetadataAsync(string sourceKey, CancellationToken ct)
    {
        var detail = await _dlsiteClient.ParseWorkDetailById(sourceKey);
        if (detail == null) return null;

        var result = new SourceDetailedMetadata
        {
            RawJson = JsonSerializer.Serialize(detail, JsonSerializerOptions.Web),
            CoverUrls = detail.CoverUrls?.ToList(),
            PredefinedFieldValues =
            {
                [nameof(DLsiteMetadataField.Introduction)] = detail.Introduction,
                [nameof(DLsiteMetadataField.Rating)] = detail.Rating,
                [nameof(DLsiteMetadataField.CoverUrls)] = detail.CoverUrls?.ToList(),
            }
        };

        // Dynamic fields from the right side of cover (声優, ジャンル, etc.)
        if (detail.PropertiesOnTheRightSideOfCover is { Count: > 0 })
        {
            foreach (var (key, values) in detail.PropertiesOnTheRightSideOfCover)
            {
                result.CustomFieldValues[key] = values;
            }
        }

        return result;
    }
}
