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
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Migration;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Search.Models.Db;
using Bootstrap.Components.Configuration.Abstractions;
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

        // 3) Migrate MediaLibraryV2 (PropertyId=24) to MediaLibraryV2Multi (PropertyId=25) in SearchFilters
        await MigrateMediaLibraryV2ToMultiInSearchFilters();
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
        var mappingService = GetRequiredService<IMediaLibraryResourceMappingService>();

        // Check if we already have mappings (skip if already migrated)
        var existingMappings = await mappingService.GetAll();
        if (existingMappings.Count > 0)
        {
            Logger.LogInformation("MediaLibraryResourceMappings already exist ({Count}), skipping migration.", existingMappings.Count);
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

        var mappings = resources
            .Where(r => validMediaLibraryIds.Contains(r.MediaLibraryId))
            .Select(r => new MediaLibraryResourceMapping
            {
                MediaLibraryId = r.MediaLibraryId,
                ResourceId = r.Id
            })
            .ToList();

        if (mappings.Any())
        {
            await mappingService.AddRange(mappings);
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

                // Create MediaLibrary mark for this path (associate path with media library)
                var mediaLibraryConfig = new MediaLibraryMarkConfig
                {
                    MatchMode = PathMatchMode.Layer,
                    Layer = 0, // Apply to all layers
                    ValueType = PropertyValueType.Fixed,
                    MediaLibraryId = library.Id,
                    ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories
                };
                pathMarks.Add(new PathMarkDbModel
                {
                    Path = normalizedPath,
                    Type = PathMarkType.MediaLibrary,
                    Priority = 0,
                    ConfigJson = JsonConvert.SerializeObject(mediaLibraryConfig),
                    SyncStatus = PathMarkSyncStatus.Synced,
                    CreatedAt = now,
                    UpdatedAt = now,
                    IsDeleted = false
                });

                // Create Resource and Property marks from template
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
                            PropertyId = (int)ResourceProperty.MediaLibraryV2Multi,
                            Operation = SearchOperation.In,
                            // Contains on MultipleChoice: filter value is the single item to find in the list
                            Value = PropertySystem.Search.SerializeFilterValue(
                                library.Id.ToString(),
                                PropertyType.MultipleChoice,
                                SearchOperation.In),
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
            return [];
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
                    Extensions = filter.Extensions?.ToList(),
                    ExtensionGroupIds = filter.ExtensionGroupIds?.ToList(),
                    ApplyScope = PathMarkApplyScope.MatchedOnly
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
            return [];
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
                    // Calculate ValueLayer based on BasePathType and Positioner
                    // Old system: Layer is relative to MediaLibrary path or Resource path
                    // New system: ValueLayer is relative to PathMark path (which is MediaLibrary path)
                    //
                    // For BasePathType.MediaLibrary + Layer mode:
                    //   Layer = 1 means first child of media library -> ValueLayer = 1
                    //   Layer = -1 means parent of media library -> ValueLayer = -1
                    //
                    // For BasePathType.Resource + Layer mode:
                    //   Layer = 0 means resource itself -> ValueLayer = 0 (special case, uses file name)
                    //   Layer = -1 means parent of resource (one level above resource) -> need to calculate
                    //   This is tricky because we don't know the resource depth at migration time
                    //   We'll use Regex mode instead for Resource base type to avoid this issue

                    int? valueLayer = null;
                    string? valueRegex = locator.Regex;
                    var matchMode = PathMatchMode.Regex;

                    if (locator.Positioner == PathPositioner.Layer && locator.Layer.HasValue)
                    {
                        if (locator.BasePathType == PathPropertyExtractorBasePathType.MediaLibrary)
                        {
                            // MediaLibrary base: Layer semantics are preserved
                            valueLayer = locator.Layer.Value;
                            matchMode = PathMatchMode.Layer;
                        }
                        else // Resource base
                        {
                            // Resource base with Layer mode is complex because resource depth varies
                            // Layer = 0 means resource itself (use ValueLayer = 0 which returns filename)
                            // Layer = -1 means parent of resource
                            // We keep Layer mode but note that ValueLayer semantics differ:
                            // In new system, ValueLayer is relative to mark path, not resource path
                            // This may not be 100% accurate but preserves most use cases
                            if (locator.Layer.Value == 0)
                            {
                                // Resource itself - this works correctly
                                valueLayer = 0;
                                matchMode = PathMatchMode.Layer;
                            }
                            else
                            {
                                // For negative layers (parent directories), we cannot accurately migrate
                                // because resource depth is not fixed. Log a warning and skip.
                                Logger.LogWarning(
                                    "Cannot migrate property locator with BasePathType=Resource and Layer={Layer} for property {Pool}/{Id}. " +
                                    "Resource-relative parent directory extraction is not supported in the new PathMark system.",
                                    locator.Layer.Value, prop.Pool, prop.Id);
                                continue;
                            }
                        }
                    }
                    else if (locator.Positioner == PathPositioner.Regex && !string.IsNullOrEmpty(locator.Regex))
                    {
                        // Regex mode: valueRegex is already set
                        matchMode = PathMatchMode.Regex;
                    }
                    else
                    {
                        // No valid extraction method, skip
                        continue;
                    }

                    var propertyConfig = new PropertyMarkConfig
                    {
                        Pool = prop.Pool,
                        PropertyId = prop.Id,
                        ValueType = PropertyValueType.Dynamic,
                        MatchMode = matchMode,
                        Layer = null,
                        Regex = null, // Null regex matches all paths
                        ValueLayer = valueLayer,
                        ValueRegex = valueRegex,
                        ApplyScope = PathMarkApplyScope.MatchedOnly
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

    /// <summary>
    /// Migrate MediaLibraryV2 (PropertyId=24, SingleChoice) to MediaLibraryV2Multi (PropertyId=25, MultipleChoice)
    /// in all search filters: SavedSearches, LastSearchV2, and RecentFilters.
    /// This ensures backward compatibility as MediaLibraryV2 is being deprecated.
    /// </summary>
    private async Task MigrateMediaLibraryV2ToMultiInSearchFilters()
    {
        var optionsManagerPool = GetRequiredService<BakabaseOptionsManagerPool>();
        var optionsManager = optionsManagerPool.Get<ResourceOptions>();
        var modified = false;

        await optionsManager.SaveAsync(options =>
        {
            // 1) Migrate SavedSearches
            if (options.SavedSearches != null && options.SavedSearches.Any())
            {
                foreach (var savedSearch in options.SavedSearches)
                {
                    if (savedSearch.Search != null)
                    {
                        if (MigrateSearchFiltersInGroup(savedSearch.Search.Group))
                        {
                            modified = true;
                            Logger.LogInformation("Migrated MediaLibraryV2 to MediaLibraryV2Multi in SavedSearch: {Name}",
                                savedSearch.Name);
                        }
                    }
                }
            }

            // 2) Migrate LastSearchV2
            if (options.LastSearchV2 != null)
            {
                if (MigrateSearchFiltersInGroup(options.LastSearchV2.Group))
                {
                    modified = true;
                    Logger.LogInformation("Migrated MediaLibraryV2 to MediaLibraryV2Multi in LastSearchV2");
                }
            }

            // 3) Migrate RecentFilters
            if (options.RecentFilters != null && options.RecentFilters.Any())
            {
                var migratedCount = 0;
                foreach (var filter in options.RecentFilters)
                {
                    if (filter.PropertyPool == PropertyPool.Internal &&
                        filter.PropertyId == (int)ResourceProperty.MediaLibraryV2)
                    {
                        // Convert MediaLibraryV2 (SingleChoice) to MediaLibraryV2Multi (MultipleChoice)
                        filter.PropertyId = (int)ResourceProperty.MediaLibraryV2Multi;

                        // The value format changes from SingleChoice to MultipleChoice
                        // SingleChoice: serialized as a single string value
                        // MultipleChoice: serialized as a JSON array of strings
                        if (!string.IsNullOrEmpty(filter.DbValue))
                        {
                            try
                            {
                                // Deserialize as SingleChoice (string)
                                var singleValue = PropertySystem.Search.DeserializeFilterValue(
                                    filter.DbValue,
                                    PropertyType.SingleChoice,
                                    filter.Operation);

                                // Re-serialize as MultipleChoice (array with single item)
                                if (singleValue != null)
                                {
                                    filter.DbValue = PropertySystem.Search.SerializeFilterValue(
                                        singleValue.ToString(),
                                        PropertyType.MultipleChoice,
                                        filter.Operation);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning(ex, "Failed to migrate RecentFilter value from MediaLibraryV2 to MediaLibraryV2Multi");
                            }
                        }

                        migratedCount++;
                        modified = true;
                    }
                }

                if (migratedCount > 0)
                {
                    Logger.LogInformation("Migrated {Count} RecentFilters from MediaLibraryV2 to MediaLibraryV2Multi",
                        migratedCount);
                }
            }
        });

        if (modified)
        {
            Logger.LogInformation("Completed migration of MediaLibraryV2 to MediaLibraryV2Multi in search filters");
        }
        else
        {
            Logger.LogInformation("No MediaLibraryV2 filters found to migrate");
        }
    }

    /// <summary>
    /// Recursively migrate MediaLibraryV2 filters to MediaLibraryV2Multi in a filter group.
    /// Returns true if any filters were migrated.
    /// </summary>
    private bool MigrateSearchFiltersInGroup(ResourceSearchFilterGroupDbModel? group)
    {
        if (group == null)
        {
            return false;
        }

        var modified = false;

        // Migrate filters in this group
        if (group.Filters != null)
        {
            foreach (var filter in group.Filters)
            {
                if (filter.PropertyPool == PropertyPool.Internal &&
                    filter.PropertyId == (int)ResourceProperty.MediaLibraryV2)
                {
                    // Convert MediaLibraryV2 (SingleChoice) to MediaLibraryV2Multi (MultipleChoice)
                    filter.PropertyId = (int)ResourceProperty.MediaLibraryV2Multi;

                    // The value format changes from SingleChoice to MultipleChoice
                    if (!string.IsNullOrEmpty(filter.Value))
                    {
                        try
                        {
                            // Deserialize as SingleChoice (string)
                            var singleValue = PropertySystem.Search.DeserializeFilterValue(
                                filter.Value,
                                PropertyType.SingleChoice,
                                filter.Operation ?? SearchOperation.Equals);

                            // Re-serialize as MultipleChoice (array with single item)
                            if (singleValue != null)
                            {
                                filter.Value = PropertySystem.Search.SerializeFilterValue(
                                    singleValue.ToString(),
                                    PropertyType.MultipleChoice,
                                    filter.Operation ?? SearchOperation.Equals);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "Failed to migrate filter value from MediaLibraryV2 to MediaLibraryV2Multi");
                        }
                    }

                    modified = true;
                }
            }
        }

        // Recursively migrate child groups
        if (group.Groups != null)
        {
            foreach (var childGroup in group.Groups)
            {
                if (MigrateSearchFiltersInGroup(childGroup))
                {
                    modified = true;
                }
            }
        }

        return modified;
    }
}
