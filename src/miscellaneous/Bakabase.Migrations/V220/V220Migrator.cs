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
using Bakabase.Modules.Property.Abstractions.Models.Db;
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

        // Clean up resources with CategoryId > 0 (historical bad data since legacy resources' categoryId should be 0)
        await CleanupResourcesWithInvalidCategoryId(dbCtx);

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
            .Where(r => r.MediaLibraryId > 0 && r.CategoryId == 0)
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

        // Get all MediaLibraryV2 with their templates (convert to domain models)
        var mediaLibraries = (await dbCtx.MediaLibrariesV2.ToListAsync()).Select(x => x.ToDomainModel()).ToArray();
        var templates = (await dbCtx.MediaLibraryTemplates.ToListAsync()).Select(t => t.ToDomainModel()).ToArray();
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
            MediaLibraryTemplate? template = null;
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

            // Create effects for the PathMarks
            await CreatePathMarkEffects(dbCtx, pathMarks);
        }

        // Create ResourceProfiles from MediaLibraryV2 + templates
        await MigrateToResourceProfiles(dbCtx, mediaLibraries, templateMap);
    }

    /// <summary>
    /// Create ResourceProfiles from MediaLibraryV2 + templates.
    /// ResourceProfiles contain enhancer settings, name template, playable file settings, and player settings.
    /// </summary>
    private async Task MigrateToResourceProfiles(BakabaseDbContext dbCtx, MediaLibraryV2[] mediaLibraries,
        Dictionary<int, MediaLibraryTemplate> templateMap)
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
            MediaLibraryTemplate? template = null;
            if (library.TemplateId.HasValue && templateMap.TryGetValue(library.TemplateId.Value, out var t))
            {
                template = t;
            }

            // Extract all settings from domain models directly
            var enhancerOptions = template != null ? ExtractEnhancerOptions(template) : null;
            var nameTemplate = template?.DisplayNameTemplate;
            var playableFileOptions = template != null ? ExtractPlayableFileOptions(template) : null;
            var playerOptions = library?.Players;
            var propertyOptions = template != null ? ExtractPropertyOptions(template) : null;

            // Skip if no settings to migrate
            if (enhancerOptions == null && string.IsNullOrEmpty(nameTemplate) &&
                playableFileOptions == null && playerOptions == null && propertyOptions == null)
            {
                continue;
            }

            var mla = PropertySystem.Builtin.MediaLibraryV2Multi;
            
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
                            PropertyPool = mla.Pool,
                            PropertyId = mla.Id,
                            Operation = SearchOperation.In,
                            Value = PropertySystem.Search.MediaLibraryV2Multi.BuildInFilterValueSerialized(library.Id),
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
                PropertiesJson = propertyOptions != null ? JsonConvert.SerializeObject(propertyOptions) : null,
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
    /// Extract ResourceProfileEnhancerOptions from MediaLibraryTemplate domain model.
    /// </summary>
    private ResourceProfileEnhancerOptions? ExtractEnhancerOptions(MediaLibraryTemplate template)
    {
        if (template.Enhancers == null || !template.Enhancers.Any())
        {
            return null;
        }

        return new ResourceProfileEnhancerOptions
        {
            Enhancers = template.Enhancers
        };
    }

    /// <summary>
    /// Extract ResourceProfilePlayableFileOptions from MediaLibraryTemplate domain model.
    /// Converts MediaLibraryTemplatePlayableFileLocator to ResourceProfilePlayableFileOptions.
    /// </summary>
    private ResourceProfilePlayableFileOptions? ExtractPlayableFileOptions(MediaLibraryTemplate template)
    {
        var locator = template.PlayableFileLocator;
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

    /// <summary>
    /// Extract ResourceProfilePropertyOptions from MediaLibraryTemplate domain model.
    /// Converts MediaLibraryTemplateProperty list to ResourceProfilePropertyOptions.
    /// </summary>
    private ResourceProfilePropertyOptions? ExtractPropertyOptions(MediaLibraryTemplate template)
    {
        if (template.Properties == null || !template.Properties.Any())
        {
            return null;
        }

        return new ResourceProfilePropertyOptions
        {
            Properties = template.Properties.Select(p => new PropertyKey(p.Pool, p.Id)).ToList()
        };
    }

    /// <summary>
    /// Convert MediaLibraryTemplate domain model to PathMark list.
    /// Extracts resource filters and property extractors from template.
    /// </summary>
    private List<PathMark> ConvertTemplateToMarks(MediaLibraryTemplate? template)
    {
        var marks = new List<PathMark>();

        if (template == null)
        {
            return [];
        }

        // Get ResourceFilters from domain model
        var resourceFilters = template.ResourceFilters;

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

        // Get Properties from domain model
        var properties = template.Properties;

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
                    //
                    // NEW SYSTEM (PathMark):
                    //   ValueLayer is always relative to PathMark path (mark path):
                    //   ValueLayer = 0: mark path's directory name itself
                    //   ValueLayer < 0 (e.g., -1, -2): go UP from mark path (parent directories)
                    //   ValueLayer > 0 (e.g., 1, 2): go DOWN from mark path (child directories, uses resource path)
                    //
                    // OLD SYSTEM (MediaLibraryTemplate):
                    //   Layer could be relative to MediaLibrary path or Resource path
                    //   - BasePathType.MediaLibrary + Layer = 0: media library path itself
                    //   - BasePathType.MediaLibrary + Layer < 0: parent directories of media library
                    //   - BasePathType.MediaLibrary + Layer > 0: child directories of media library (relative to resource)
                    //   - BasePathType.Resource: relative to resource path
                    //
                    // MIGRATION RULES:
                    // - BasePathType.MediaLibrary + Layer = 0: maps to ValueLayer = 0
                    // - BasePathType.MediaLibrary + Layer < 0: maps to ValueLayer = Layer (same negative value)
                    // - BasePathType.MediaLibrary + Layer > 0: maps to ValueLayer = Layer (now supported with resource path)
                    // - BasePathType.Resource: CANNOT migrate (was purely resource-based)

                    int? valueLayer = null;
                    string? valueRegex = locator.Regex;
                    var matchMode = PathMatchMode.Regex;

                    if (locator.Positioner == PathPositioner.Layer && locator.Layer.HasValue)
                    {
                        if (locator.BasePathType == PathPropertyExtractorBasePathType.MediaLibrary)
                        {
                            // All MediaLibrary-based layer values can be migrated directly
                            // Layer = 0: mark path itself
                            // Layer < 0: go UP (parent directories)
                            // Layer > 0: go DOWN (child directories, extracted using resource path)
                            valueLayer = locator.Layer.Value;
                            matchMode = PathMatchMode.Layer;
                        }
                        else // Resource base
                        {
                            // Resource base was extracting purely from resource path, which cannot be mapped
                            // to the new mark-centric system
                            Logger.LogWarning(
                                "Cannot migrate property locator with BasePathType=Resource for property {Pool}/{Id}. " +
                                "Pure resource-based extraction is not supported in the new system. Please reconfigure this property mark manually.",
                                prop.Pool, prop.Id);
                            continue;
                        }
                    }
                    else if (locator.Positioner == PathPositioner.Regex && !string.IsNullOrEmpty(locator.Regex))
                    {
                        // Regex mode: apply regex on mark path's directory name
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
    /// Migrate MediaLibraryV2 to MediaLibraryV2Multi in all search filters: SavedSearches, LastSearchV2, and RecentFilters.
    /// This ensures backward compatibility as MediaLibraryV2 is being deprecated.
    /// </summary>
    private async Task MigrateMediaLibraryV2ToMultiInSearchFilters()
    {
        // Get property definitions from PropertySystem - we don't hardcode property types
        var fromProperty = PropertySystem.Builtin.Get(ResourceProperty.MediaLibraryV2);
        var toProperty = PropertySystem.Builtin.Get(ResourceProperty.MediaLibraryV2Multi);

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
                        if (MigrateSearchFiltersInGroup(savedSearch.Search.Group, fromProperty, toProperty))
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
                if (MigrateSearchFiltersInGroup(options.LastSearchV2.Group, fromProperty, toProperty))
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
                    if (filter.PropertyPool == fromProperty.Pool &&
                        filter.PropertyId == fromProperty.Id)
                    {
                        // Update property reference to target property
                        filter.PropertyId = toProperty.Id;

                        // Convert filter value using property definitions
                        if (!string.IsNullOrEmpty(filter.DbValue))
                        {
                            try
                            {
                                filter.DbValue = PropertySystem.Search.ConvertFilterValue(
                                    filter.DbValue,
                                    fromProperty,
                                    toProperty,
                                    filter.Operation);
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
    /// Recursively migrate filters from one property to another in a filter group.
    /// Returns true if any filters were migrated.
    /// </summary>
    /// <param name="group">The filter group to migrate</param>
    /// <param name="fromProperty">The source property definition</param>
    /// <param name="toProperty">The target property definition</param>
    private bool MigrateSearchFiltersInGroup(
        ResourceSearchFilterGroupDbModel? group,
        Property fromProperty,
        Property toProperty)
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
                if (filter.PropertyPool == fromProperty.Pool &&
                    filter.PropertyId == fromProperty.Id)
                {
                    // Update property reference to target property
                    filter.PropertyId = toProperty.Id;

                    // Convert filter value using property definitions
                    if (!string.IsNullOrEmpty(filter.Value))
                    {
                        try
                        {
                            filter.Value = PropertySystem.Search.ConvertFilterValue(
                                filter.Value,
                                fromProperty,
                                toProperty,
                                filter.Operation ?? SearchOperation.Equals);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "Failed to migrate filter value from {FromProperty} to {ToProperty}",
                                fromProperty.Id, toProperty.Id);
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
                if (MigrateSearchFiltersInGroup(childGroup, fromProperty, toProperty))
                {
                    modified = true;
                }
            }
        }

        return modified;
    }

    /// <summary>
    /// Create ResourceMarkEffect and PropertyMarkEffect records for migrated PathMarks.
    /// This ensures the system can track which resources/properties were created by which marks.
    /// </summary>
    private async Task CreatePathMarkEffects(BakabaseDbContext dbCtx, List<PathMarkDbModel> pathMarks)
    {
        var now = DateTime.UtcNow;

        // Get all existing resources
        var allResources = await dbCtx.ResourcesV2.ToListAsync();
        if (!allResources.Any())
        {
            Logger.LogInformation("No existing resources found, skipping PathMark effect creation.");
            return;
        }

        // Build a path-to-resource lookup for efficiency
        var resourcesByPath = allResources
            .GroupBy(r => r.Path.StandardizePath()!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Get existing media library mappings
        var existingMappings = await dbCtx.MediaLibraryResourceMappings.ToListAsync();
        var mappingLookup = existingMappings
            .GroupBy(m => m.ResourceId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.MediaLibraryId).ToHashSet());

        var resourceMarkEffects = new List<ResourceMarkEffectDbModel>();
        var propertyMarkEffects = new List<PropertyMarkEffectDbModel>();

        foreach (var pathMark in pathMarks)
        {
            var markPath = pathMark.Path.StandardizePath()!;

            // Find all resources under this path
            var resourcesUnderPath = allResources
                .Where(r => IsPathUnderParent(r.Path.StandardizePath()!, markPath))
                .ToList();

            if (!resourcesUnderPath.Any())
            {
                continue;
            }

            switch (pathMark.Type)
            {
                case PathMarkType.Resource:
                {
                    // For Resource marks, create ResourceMarkEffect for each resource under the path
                    var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(pathMark.ConfigJson);
                    if (config == null) continue;

                    // Filter resources based on config
                    var matchedResources = FilterResourcesByResourceMarkConfig(resourcesUnderPath, markPath, config);

                    foreach (var resource in matchedResources)
                    {
                        resourceMarkEffects.Add(new ResourceMarkEffectDbModel
                        {
                            MarkId = pathMark.Id,
                            Path = resource.Path.StandardizePath()!,
                            CreatedAt = now
                        });
                    }
                    break;
                }

                case PathMarkType.MediaLibrary:
                {
                    // For MediaLibrary marks, create PropertyMarkEffect for each resource with matching mapping
                    var config = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(pathMark.ConfigJson);
                    if (config?.MediaLibraryId == null) continue;

                    // Filter resources based on config
                    var matchedResources = FilterResourcesByMediaLibraryMarkConfig(resourcesUnderPath, markPath, config);

                    foreach (var resource in matchedResources)
                    {
                        // Check if this resource has the media library mapping
                        if (mappingLookup.TryGetValue(resource.Id, out var resourceMlIds) &&
                            resourceMlIds.Contains(config.MediaLibraryId.Value))
                        {
                            propertyMarkEffects.Add(new PropertyMarkEffectDbModel
                            {
                                MarkId = pathMark.Id,
                                PropertyPool = (int)PropertyPool.Internal,
                                PropertyId = 25, // MediaLibraryV2Multi
                                ResourceId = resource.Id,
                                Value = config.MediaLibraryId.Value.ToString(),
                                Priority = pathMark.Priority,
                                CreatedAt = now,
                                UpdatedAt = now
                            });
                        }
                    }
                    break;
                }

                case PathMarkType.Property:
                {
                    // For Property marks, create PropertyMarkEffect for each resource with matching property value
                    var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(pathMark.ConfigJson);
                    if (config == null) continue;

                    // Only handle Custom properties for now
                    if (config.Pool != PropertyPool.Custom) continue;

                    // Get existing property values for these resources
                    var resourceIds = resourcesUnderPath.Select(r => r.Id).ToList();
                    var existingPropertyValues = await dbCtx.CustomPropertyValues
                        .Where(pv => resourceIds.Contains(pv.ResourceId) &&
                                     pv.PropertyId == config.PropertyId &&
                                     pv.Scope == (int)PropertyValueScope.Synchronization)
                        .ToListAsync();

                    var pvByResourceId = existingPropertyValues.ToDictionary(pv => pv.ResourceId);

                    // Filter resources based on config
                    var matchedResources = FilterResourcesByPropertyMarkConfig(resourcesUnderPath, markPath, config);

                    foreach (var resource in matchedResources)
                    {
                        if (pvByResourceId.TryGetValue(resource.Id, out var pv) && !string.IsNullOrEmpty(pv.Value))
                        {
                            propertyMarkEffects.Add(new PropertyMarkEffectDbModel
                            {
                                MarkId = pathMark.Id,
                                PropertyPool = (int)config.Pool,
                                PropertyId = config.PropertyId,
                                ResourceId = resource.Id,
                                Value = pv.Value,
                                Priority = pathMark.Priority,
                                CreatedAt = now,
                                UpdatedAt = now
                            });
                        }
                    }
                    break;
                }
            }
        }

        // Save effects
        if (resourceMarkEffects.Any())
        {
            await dbCtx.ResourceMarkEffects.AddRangeAsync(resourceMarkEffects);
            await dbCtx.SaveChangesAsync();
            Logger.LogInformation("Created {Count} ResourceMarkEffects from PathMarks.", resourceMarkEffects.Count);
        }

        if (propertyMarkEffects.Any())
        {
            await dbCtx.PropertyMarkEffects.AddRangeAsync(propertyMarkEffects);
            await dbCtx.SaveChangesAsync();
            Logger.LogInformation("Created {Count} PropertyMarkEffects from PathMarks.", propertyMarkEffects.Count);
        }
    }

    /// <summary>
    /// Check if a path is under a parent path.
    /// </summary>
    private bool IsPathUnderParent(string path, string parentPath)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(parentPath))
            return false;

        var normalizedPath = path.StandardizePath()!;
        var normalizedParent = parentPath.StandardizePath()!;

        return normalizedPath.StartsWith(normalizedParent + "/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Filter resources based on ResourceMarkConfig.
    /// Simplified version for migration - matches by layer or regex as configured.
    /// </summary>
    private List<ResourceDbModel> FilterResourcesByResourceMarkConfig(
        List<ResourceDbModel> resources,
        string markPath,
        ResourceMarkConfig config)
    {
        var markSegments = GetPathSegments(markPath);

        if (config.MatchMode == PathMatchMode.Layer && config.Layer.HasValue)
        {
            var targetDepth = config.Layer.Value;
            var includeSubdirectories = config.ApplyScope == PathMarkApplyScope.MatchedAndSubdirectories;

            return resources.Where(r =>
            {
                var resourceSegments = GetPathSegments(r.Path.StandardizePath()!);
                var relativeDepth = resourceSegments.Length - markSegments.Length;

                if (targetDepth < 0)
                {
                    // Negative layer (parent) - not typically used for resources
                    return false;
                }

                if (includeSubdirectories)
                {
                    return relativeDepth >= targetDepth;
                }

                return relativeDepth == targetDepth;
            }).ToList();
        }

        if (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.Regex))
        {
            try
            {
                var regex = new System.Text.RegularExpressions.Regex(config.Regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var includeSubdirectories = config.ApplyScope == PathMarkApplyScope.MatchedAndSubdirectories;

                var matched = resources.Where(r =>
                {
                    var standardizedPath = r.Path.StandardizePath()!;
                    var relativePath = standardizedPath.Substring(markPath.Length).TrimStart('/');
                    return regex.IsMatch(relativePath);
                }).ToList();

                if (!includeSubdirectories)
                {
                    return matched;
                }

                // Include resources under matched paths
                var matchedPaths = matched.Select(r => r.Path.StandardizePath()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return resources.Where(r =>
                {
                    var path = r.Path.StandardizePath()!;
                    return matchedPaths.Contains(path) ||
                           matchedPaths.Any(mp => IsPathUnderParent(path, mp));
                }).ToList();
            }
            catch
            {
                return new List<ResourceDbModel>();
            }
        }

        return new List<ResourceDbModel>();
    }

    /// <summary>
    /// Filter resources based on MediaLibraryMarkConfig.
    /// </summary>
    private List<ResourceDbModel> FilterResourcesByMediaLibraryMarkConfig(
        List<ResourceDbModel> resources,
        string markPath,
        MediaLibraryMarkConfig config)
    {
        var markSegments = GetPathSegments(markPath);

        if (config.MatchMode == PathMatchMode.Layer && config.Layer.HasValue)
        {
            var targetDepth = config.Layer.Value;
            var includeSubdirectories = config.ApplyScope == PathMarkApplyScope.MatchedAndSubdirectories;

            return resources.Where(r =>
            {
                var resourceSegments = GetPathSegments(r.Path.StandardizePath()!);
                var relativeDepth = resourceSegments.Length - markSegments.Length;

                if (includeSubdirectories)
                {
                    return relativeDepth >= targetDepth;
                }

                return relativeDepth == targetDepth;
            }).ToList();
        }

        // For regex mode or default, return all resources under path
        return resources;
    }

    /// <summary>
    /// Filter resources based on PropertyMarkConfig.
    /// </summary>
    private List<ResourceDbModel> FilterResourcesByPropertyMarkConfig(
        List<ResourceDbModel> resources,
        string markPath,
        PropertyMarkConfig config)
    {
        var markSegments = GetPathSegments(markPath);

        if (config.MatchMode == PathMatchMode.Layer && config.Layer.HasValue)
        {
            var targetDepth = config.Layer.Value;
            var includeSubdirectories = config.ApplyScope == PathMarkApplyScope.MatchedAndSubdirectories;

            return resources.Where(r =>
            {
                var resourceSegments = GetPathSegments(r.Path.StandardizePath()!);
                var relativeDepth = resourceSegments.Length - markSegments.Length;

                if (includeSubdirectories)
                {
                    return relativeDepth >= targetDepth;
                }

                return relativeDepth == targetDepth;
            }).ToList();
        }

        // For regex mode or default, return all resources under path
        return resources;
    }

    /// <summary>
    /// Get path segments from a standardized path.
    /// </summary>
    private string[] GetPathSegments(string path)
    {
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Clean up resources with CategoryId > 0.
    /// Since legacy resources' categoryId has been set to 0, any resource with CategoryId > 0
    /// is historical bad data that should be removed.
    /// </summary>
    private async Task CleanupResourcesWithInvalidCategoryId(BakabaseDbContext dbCtx)
    {
        var invalidResources = await dbCtx.ResourcesV2
            .Where(r => r.CategoryId > 0)
            .ToListAsync();

        if (!invalidResources.Any())
        {
            Logger.LogInformation("No resources with CategoryId > 0 found, skipping cleanup.");
            return;
        }

        Logger.LogInformation("Found {Count} resources with CategoryId > 0, deleting as historical bad data.",
            invalidResources.Count);

        dbCtx.ResourcesV2.RemoveRange(invalidResources);
        await dbCtx.SaveChangesAsync();

        Logger.LogInformation("Deleted {Count} resources with invalid CategoryId.", invalidResources.Count);
    }
}
