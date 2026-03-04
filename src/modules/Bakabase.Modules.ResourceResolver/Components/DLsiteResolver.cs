using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.ResourceResolver.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ResourceResolver.Components;

/// <summary>
/// Discovers DLsite works as resources.
/// Uses cached DLsiteWork data from DLsiteWorkService.
/// Metadata retrieval is handled by the existing DLsiteEnhancer.
/// </summary>
public class DLsiteResolver : IResourceResolver
{
    private readonly IDLsiteWorkService _workService;
    private readonly ILogger<DLsiteResolver> _logger;
    private static readonly Regex WorkIdPattern = new(@"[RBV]J\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DLsiteResolver(IDLsiteWorkService workService, ILogger<DLsiteResolver> logger)
    {
        _workService = workService;
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

            resources.Add(new ResolvedResource
            {
                SourceKey = work.WorkId,
                DisplayName = work.Title ?? work.WorkId,
                Path = work.IsDownloaded ? work.LocalPath : null,
                Source = ResourceSource.DLsite
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
            UseLocaleEmulator = true
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
}
