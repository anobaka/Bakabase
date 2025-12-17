using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Migration;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Search.Models.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.Migrations.V220;

/// <summary>
/// Migrator for V2.2.0: Media Library Refactoring
/// Migration flow:
/// 0) First migrate early version Category+MediaLibrary(v1) to MediaLibraryV2+MediaLibraryTemplate
/// 1) Migrate Resource.MediaLibraryId to MediaLibraryResourceMapping table
/// 2) Convert MediaLibraryV2 + MediaLibraryTemplate to PathMark + ResourceProfile (marked as synced)
/// </summary>
public class V220Migrator : AbstractMigrator
{
    public V220Migrator(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override string ApplyOnVersionEqualsOrBeforeString => "2.2.0-beta";

    protected override async Task MigrateAfterDbMigrationInternal(object? context)
    {
        var dbCtx = GetRequiredService<BakabaseDbContext>();

        // 0) First migrate early version Category+MediaLibrary(v1) to MediaLibraryV2+MediaLibraryTemplate
        await MigrateLegacyCategoryMediaLibrary();

        // 1) Migrate Resource.MediaLibraryId to MediaLibraryResourceMapping
        await MigrateResourceMediaLibraryMappings(dbCtx);

        // 2) Convert MediaLibraryV2 + Template to PathMark + ResourceProfile (marked as synced)
        await MigrateMediaLibraryTemplateToPathMarkAndResourceProfile(dbCtx);
    }

    /// <summary>
    /// Migrate early version Category+MediaLibrary(v1) to MediaLibraryV2+MediaLibraryTemplate.
    /// This step is for users upgrading from versions before MediaLibraryV2 was introduced.
    /// Skips if MediaLibraryV2 or MediaLibraryTemplate already exist to prevent duplicate data.
    /// </summary>
    private async Task MigrateLegacyCategoryMediaLibrary()
    {
        try
        {
            var dbCtx = GetRequiredService<BakabaseDbContext>();

            // Skip if MediaLibraryV2 or MediaLibraryTemplate already exist to prevent duplicate data
            var hasMediaLibraryV2 = await dbCtx.MediaLibrariesV2.AnyAsync();
            var hasMediaLibraryTemplate = await dbCtx.MediaLibraryTemplates.AnyAsync();

            if (hasMediaLibraryV2 || hasMediaLibraryTemplate)
            {
                Logger.LogInformation(
                    "MediaLibraryV2 ({HasV2}) or MediaLibraryTemplate ({HasTemplate}) already exist, skipping legacy migration.",
                    hasMediaLibraryV2, hasMediaLibraryTemplate);
                return;
            }

            var migrationHelper = GetRequiredService<MigrationHelper>();
            await migrationHelper.MigrateCategoriesMediaLibrariesAndResources();
            Logger.LogInformation("Completed migration from Category+MediaLibrary(v1) to MediaLibraryV2+MediaLibraryTemplate.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "MigrationHelper failed or is not available. This is expected if the codebase no longer supports legacy Category+MediaLibrary. Continuing with V220 migration.");
        }
    }

    /// <summary>
    /// Migrate existing Resource.MediaLibraryId to MediaLibraryResourceMapping table.
    /// This preserves the original relationship while allowing resources to belong to multiple media libraries.
    /// </summary>
    private async Task MigrateResourceMediaLibraryMappings(BakabaseDbContext dbCtx)
    {
        // Check if we already have mappings (skip if already migrated)
        var existingMappingsCount = await dbCtx.MediaLibraryResourceMappings.CountAsync();
        if (existingMappingsCount > 0)
        {
            Logger.LogInformation("MediaLibraryResourceMappings already exist ({Count}), skipping migration.", existingMappingsCount);
            return;
        }

        // Get all resources with valid MediaLibraryId
        var resources = await dbCtx.ResourcesV2
            .Where(r => r.MediaLibraryId > 0)
            .Select(r => new { r.Id, r.MediaLibraryId })
            .ToListAsync();

        if (!resources.Any())
        {
            Logger.LogInformation("No resources with MediaLibraryId found, skipping mapping migration.");
            return;
        }

        // Get valid MediaLibraryV2 IDs
        var validMediaLibraryIds = await dbCtx.MediaLibrariesV2
            .Select(m => m.Id)
            .ToHashSetAsync();

        var now = DateTime.UtcNow;
        var mappings = resources
            .Where(r => validMediaLibraryIds.Contains(r.MediaLibraryId))
            .Select(r => new MediaLibraryResourceMappingDbModel
            {
                MediaLibraryId = r.MediaLibraryId,
                ResourceId = r.Id,
                CreateDt = now
            })
            .ToList();

        if (mappings.Any())
        {
            await dbCtx.MediaLibraryResourceMappings.AddRangeAsync(mappings);
            await dbCtx.SaveChangesAsync();
            Logger.LogInformation("Migrated {Count} resource-media library mappings.", mappings.Count);
        }

        // Log resources with invalid MediaLibraryId for debugging
        var orphanedResources = resources.Where(r => !validMediaLibraryIds.Contains(r.MediaLibraryId)).ToList();
        if (orphanedResources.Any())
        {
            Logger.LogWarning("Found {Count} resources with invalid MediaLibraryId (not in MediaLibrariesV2).",
                orphanedResources.Count);
        }
    }

    /// <summary>
    /// Convert MediaLibraryV2 + MediaLibraryTemplate to PathMark and ResourceProfile.
    /// Each MediaLibraryV2.Path gets PathMarks derived from the template.
    /// PathMarks are marked as Synced since the data is already synchronized.
    /// </summary>
    private async Task MigrateMediaLibraryTemplateToPathMarkAndResourceProfile(BakabaseDbContext dbCtx)
    {
        // Check if we already have PathMarks (skip if already migrated)
        var existingPathMarksCount = await dbCtx.PathMarks.CountAsync();
        if (existingPathMarksCount > 0)
        {
            Logger.LogInformation("PathMarks already exist ({Count}), skipping migration.", existingPathMarksCount);
            return;
        }

        // Get all MediaLibraryV2 with their templates
        var mediaLibraries = (await dbCtx.MediaLibrariesV2.ToListAsync()).Select(x => x.ToDomainModel()).ToArray();
        var templates = await dbCtx.MediaLibraryTemplates.ToListAsync();
        var templateMap = templates.ToDictionary(t => t.Id);

        var now = DateTime.UtcNow;
        var pathMarks = new List<PathMarkDbModel>();
        var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var library in mediaLibraries)
        {
            // Parse paths from JSON
            var paths = library.Paths;

            if (!paths.Any())
            {
                continue;
            }

            // Get template if exists
            MediaLibraryTemplateDbModel? template = null;
            if (library.TemplateId.HasValue && templateMap.TryGetValue(library.TemplateId.Value, out var t))
            {
                template = t;
            }

            // Create PathMarks for each path
            foreach (var path in paths)
            {
                var normalizedPath = path.StandardizePath();
                if (string.IsNullOrEmpty(normalizedPath))
                {
                    continue;
                }

                // Check if path already processed
                if (processedPaths.Contains(normalizedPath))
                {
                    Logger.LogWarning("Duplicate path found, skipping: {Path}", normalizedPath);
                    continue;
                }
                processedPaths.Add(normalizedPath);

                var marks = ConvertTemplateToMarks(template);
                foreach (var mark in marks)
                {
                    var pathMark = new PathMarkDbModel
                    {
                        Path = normalizedPath,
                        Type = mark.Type,
                        Priority = mark.Priority,
                        ConfigJson = mark.ConfigJson,
                        SyncStatus = PathMarkSyncStatus.Synced, // Mark as Synced since data is already synchronized
                        CreatedAt = now,
                        UpdatedAt = now,
                        IsDeleted = false
                    };
                    pathMarks.Add(pathMark);
                }
            }
        }

        if (pathMarks.Any())
        {
            await dbCtx.PathMarks.AddRangeAsync(pathMarks);
            await dbCtx.SaveChangesAsync();
            Logger.LogInformation("Created {Count} PathMarks from MediaLibraryV2 (marked as Synced).", pathMarks.Count);
        }

        // Create ResourceProfiles from MediaLibraryV2 + templates
        var mediaLibraryDbModels = await dbCtx.MediaLibrariesV2.ToListAsync();
        var mediaLibraryDbMap = mediaLibraryDbModels.ToDictionary(m => m.Id);
        await MigrateToResourceProfiles(dbCtx, mediaLibraries, templateMap, mediaLibraryDbMap);
    }

    /// <summary>
    /// Create ResourceProfiles from MediaLibraryV2 + templates.
    /// ResourceProfiles contain enhancer settings, name template, playable file settings, and player settings.
    /// </summary>
    private async Task MigrateToResourceProfiles(BakabaseDbContext dbCtx, MediaLibraryV2[] mediaLibraries,
        Dictionary<int, MediaLibraryTemplateDbModel> templateMap, Dictionary<int, MediaLibraryV2DbModel> mediaLibraryDbMap)
    {
        // Check if we already have ResourceProfiles (skip if already migrated)
        var existingProfilesCount = await dbCtx.ResourceProfiles.CountAsync();
        if (existingProfilesCount > 0)
        {
            Logger.LogInformation("ResourceProfiles already exist ({Count}), skipping migration.", existingProfilesCount);
            return;
        }

        var now = DateTime.UtcNow;
        var profiles = new List<ResourceProfileDbModel>();

        foreach (var library in mediaLibraries)
        {
            MediaLibraryTemplateDbModel? template = null;
            if (library.TemplateId.HasValue && templateMap.TryGetValue(library.TemplateId.Value, out var t))
            {
                template = t;
            }

            // Get raw DB model to access Players field
            mediaLibraryDbMap.TryGetValue(library.Id, out var libraryDbModel);

            // Extract all settings
            var enhancerOptions = template != null ? ExtractEnhancerOptions(template) : null;
            var nameTemplate = template?.DisplayNameTemplate;
            var playableFileOptions = template != null ? ExtractPlayableFileOptions(template) : null;
            var playerOptions = libraryDbModel != null ? ExtractPlayerOptions(libraryDbModel) : null;

            // Skip if no settings to migrate
            if (enhancerOptions == null && string.IsNullOrEmpty(nameTemplate) &&
                playableFileOptions == null && playerOptions == null)
            {
                continue;
            }

            // Create search using ResourceSearchDbModel structure
            var searchDbModel = new ResourceSearchDbModel
            {
                Group = new ResourceSearchFilterGroupDbModel
                {
                    Combinator = SearchCombinator.And,
                    Disabled = false,
                    Filters =
                    [
                        new ResourceSearchFilterDbModel
                        {
                            PropertyPool = PropertyPool.Internal,
                            PropertyId = (int)ResourceProperty.MediaLibraryV2,
                            Operation = SearchOperation.Equals,
                            Value =  library.Id.ToString(),
                            Disabled = false
                        }
                    ]
                },
                Page = 1,
                PageSize = int.MaxValue
            };

            var profile = new ResourceProfileDbModel
            {
                Name = $"Profile from {library.Name}",
                Priority = 0,
                SearchJson = JsonConvert.SerializeObject(searchDbModel),
                NameTemplate = nameTemplate,
                EnhancerSettingsJson = enhancerOptions != null ? JsonConvert.SerializeObject(enhancerOptions) : null,
                PlayableFileSettingsJson = playableFileOptions != null ? JsonConvert.SerializeObject(playableFileOptions) : null,
                PlayerSettingsJson = playerOptions != null ? JsonConvert.SerializeObject(playerOptions) : null,
                CreatedAt = now,
                UpdatedAt = now,
            };
            profiles.Add(profile);
        }

        if (profiles.Any())
        {
            await dbCtx.ResourceProfiles.AddRangeAsync(profiles);
            await dbCtx.SaveChangesAsync();
            Logger.LogInformation("Created {Count} ResourceProfiles from MediaLibraryV2.", profiles.Count);
        }
    }

    /// <summary>
    /// Extract ResourceProfileEnhancerOptions from MediaLibraryTemplate.
    /// </summary>
    private ResourceProfileEnhancerOptions? ExtractEnhancerOptions(MediaLibraryTemplateDbModel template)
    {
        if (string.IsNullOrEmpty(template.Enhancers))
        {
            return null;
        }

        try
        {
            var enhancers = JsonConvert.DeserializeObject<List<EnhancerFullOptions>>(template.Enhancers);
            if (enhancers == null || !enhancers.Any())
            {
                return null;
            }

            return new ResourceProfileEnhancerOptions
            {
                Enhancers = enhancers.Select(e => e).ToList()
            };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse Enhancers from template {Id}", template.Id);
            return null;
        }
    }

    /// <summary>
    /// Extract ResourceProfilePlayableFileOptions from MediaLibraryTemplate.
    /// Converts MediaLibraryTemplatePlayableFileLocator to ResourceProfilePlayableFileOptions.
    /// </summary>
    private ResourceProfilePlayableFileOptions? ExtractPlayableFileOptions(MediaLibraryTemplateDbModel template)
    {
        if (string.IsNullOrEmpty(template.PlayableFileLocator))
        {
            return null;
        }

        try
        {
            var locator = JsonConvert.DeserializeObject<MediaLibraryTemplatePlayableFileLocator>(template.PlayableFileLocator);
            if (locator == null)
            {
                return null;
            }

            // Collect all extensions from ExtensionGroups and Extensions
            var allExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (locator.Extensions != null)
            {
                foreach (var ext in locator.Extensions)
                {
                    allExtensions.Add(ext);
                }
            }

            if (locator.ExtensionGroups != null)
            {
                foreach (var group in locator.ExtensionGroups)
                {
                    if (group.Extensions != null)
                    {
                        foreach (var ext in group.Extensions)
                        {
                            allExtensions.Add(ext);
                        }
                    }
                }
            }

            if (!allExtensions.Any())
            {
                return null;
            }

            return new ResourceProfilePlayableFileOptions
            {
                Extensions = allExtensions.ToList()
            };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse PlayableFileLocator from template {Id}", template.Id);
            return null;
        }
    }

    /// <summary>
    /// Extract ResourceProfilePlayerOptions from MediaLibraryV2.
    /// </summary>
    private ResourceProfilePlayerOptions? ExtractPlayerOptions(MediaLibraryV2DbModel library)
    {
        if (string.IsNullOrEmpty(library.Players))
        {
            return null;
        }

        try
        {
            var players = JsonConvert.DeserializeObject<List<MediaLibraryPlayer>>(library.Players);
            if (players == null || !players.Any())
            {
                return null;
            }

            return new ResourceProfilePlayerOptions
            {
                Players = players
            };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse Players from MediaLibraryV2 {Id}", library.Id);
            return null;
        }
    }

    /// <summary>
    /// Convert MediaLibraryTemplate to PathMark list.
    /// Extracts resource filters and property extractors from template.
    /// </summary>
    private List<PathMark> ConvertTemplateToMarks(MediaLibraryTemplateDbModel? template)
    {
        var marks = new List<PathMark>();

        if (template == null)
        {
            // No template - create default resource mark (layer 1)
            var defaultResourceConfig = new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = null, // Both files and directories
                Extensions = null
            };
            marks.Add(new PathMark
            {
                Type = PathMarkType.Resource,
                Priority = 0,
                ConfigJson = JsonConvert.SerializeObject(defaultResourceConfig)
            });
            return marks;
        }

        // Parse ResourceFilters from template
        List<PathFilter>? resourceFilters = null;
        if (!string.IsNullOrEmpty(template.ResourceFilters))
        {
            try
            {
                resourceFilters = JsonConvert.DeserializeObject<List<PathFilter>>(template.ResourceFilters);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to parse ResourceFilters for template {Id}", template.Id);
            }
        }

        // Create Resource marks from filters
        if (resourceFilters != null && resourceFilters.Any())
        {
            int priority = 0;
            foreach (var filter in resourceFilters)
            {
                var resourceConfig = new ResourceMarkConfig
                {
                    MatchMode = filter.Positioner == PathPositioner.Regex ? PathMatchMode.Regex : PathMatchMode.Layer,
                    Layer = filter.Layer,
                    Regex = filter.Regex,
                    FsTypeFilter = filter.FsType,
                    Extensions = filter.Extensions?.ToList()
                };
                marks.Add(new PathMark
                {
                    Type = PathMarkType.Resource,
                    Priority = priority++,
                    ConfigJson = JsonConvert.SerializeObject(resourceConfig)
                });
            }
        }
        else
        {
            // No resource filters - create default (layer 1)
            var defaultResourceConfig = new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = null,
                Extensions = null
            };
            marks.Add(new PathMark
            {
                Type = PathMarkType.Resource,
                Priority = 0,
                ConfigJson = JsonConvert.SerializeObject(defaultResourceConfig)
            });
        }

        // Parse Properties from template
        List<MediaLibraryTemplateProperty>? properties = null;
        if (!string.IsNullOrEmpty(template.Properties))
        {
            try
            {
                properties = JsonConvert.DeserializeObject<List<MediaLibraryTemplateProperty>>(template.Properties);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to parse Properties for template {Id}", template.Id);
            }
        }

        // Create Property marks from template properties
        if (properties != null && properties.Any())
        {
            int propertyPriority = 100; // Start property priority after resource marks
            foreach (var prop in properties)
            {
                if (prop.ValueLocators == null || !prop.ValueLocators.Any())
                {
                    continue;
                }

                foreach (var locator in prop.ValueLocators)
                {
                    var propertyConfig = new PropertyMarkConfig
                    {
                        Pool = prop.Pool,
                        PropertyId = prop.Id,
                        ValueType = PropertyValueType.Dynamic, // From path extraction
                        MatchMode = locator.Positioner == PathPositioner.Regex ? PathMatchMode.Regex : PathMatchMode.Layer,
                        Layer = locator.Layer,
                        Regex = locator.Regex
                    };
                    marks.Add(new PathMark
                    {
                        Type = PathMarkType.Property,
                        Priority = propertyPriority++,
                        ConfigJson = JsonConvert.SerializeObject(propertyConfig)
                    });
                }
            }
        }

        return marks;
    }
}
