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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.Migrations.V220;

/// <summary>
/// Migrator for V2.2.0: Media Library Refactoring
/// - Migrates Resource.MediaLibraryId to MediaLibraryResourceMapping table
/// - Converts MediaLibraryV2 + MediaLibraryTemplate to PathRule
/// </summary>
public class V220Migrator : AbstractMigrator
{
    public V220Migrator(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override string ApplyOnVersionEqualsOrBeforeString => "2.2.0-beta";

    protected override async Task MigrateAfterDbMigrationInternal(object? context)
    {
        var dbCtx = GetRequiredService<InsideWorldDbContext>();

        // 1) Migrate Resource.MediaLibraryId to MediaLibraryResourceMapping
        await MigrateResourceMediaLibraryMappings(dbCtx);

        // 2) Convert MediaLibraryV2 + Template to PathRule
        await MigrateMediaLibraryTemplateToPathRule(dbCtx);
    }

    /// <summary>
    /// Migrate existing Resource.MediaLibraryId to MediaLibraryResourceMapping table.
    /// This preserves the original relationship while allowing resources to belong to multiple media libraries.
    /// </summary>
    private async Task MigrateResourceMediaLibraryMappings(InsideWorldDbContext dbCtx)
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
                Source = MappingSource.Rule, // Mark as Rule since they were auto-assigned
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
    /// Convert MediaLibraryV2 + MediaLibraryTemplate to PathRule.
    /// Each MediaLibraryV2.Path becomes a PathRule with marks derived from the template.
    /// </summary>
    private async Task MigrateMediaLibraryTemplateToPathRule(InsideWorldDbContext dbCtx)
    {
        // Check if we already have PathRules (skip if already migrated)
        var existingPathRulesCount = await dbCtx.PathRules.CountAsync();
        if (existingPathRulesCount > 0)
        {
            Logger.LogInformation("PathRules already exist ({Count}), skipping migration.", existingPathRulesCount);
            return;
        }

        // Get all MediaLibraryV2 with their templates
        var mediaLibraries = (await dbCtx.MediaLibrariesV2.ToListAsync()).Select(x => x.ToDomainModel()).ToArray();
        var templates = await dbCtx.MediaLibraryTemplates.ToListAsync();
        var templateMap = templates.ToDictionary(t => t.Id);

        var now = DateTime.UtcNow;
        var pathRules = new List<PathRuleDbModel>();

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

            // Create PathRule for each path
            foreach (var path in paths)
            {
                var normalizedPath = path.StandardizePath();
                if (string.IsNullOrEmpty(normalizedPath))
                {
                    continue;
                }

                // Check if path already exists
                if (pathRules.Any(r => r.Path.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    Logger.LogWarning("Duplicate path found, skipping: {Path}", normalizedPath);
                    continue;
                }

                var marks = ConvertTemplateToMarks(template);
                var pathRule = new PathRuleDbModel
                {
                    Path = normalizedPath,
                    MarksJson = JsonConvert.SerializeObject(marks),
                    CreateDt = now,
                    UpdateDt = now
                };
                pathRules.Add(pathRule);
            }
        }

        if (pathRules.Any())
        {
            await dbCtx.PathRules.AddRangeAsync(pathRules);
            await dbCtx.SaveChangesAsync();
            Logger.LogInformation("Created {Count} PathRules from MediaLibraryV2.", pathRules.Count);
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
