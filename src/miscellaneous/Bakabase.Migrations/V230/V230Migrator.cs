using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.StandardValue.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Migrations.V230;

/// <summary>
/// Migrator for V2.3.0: Migrate PlayableFilePaths to PlayableItems in resource cache.
/// PlayableFilePaths stores a ListString of file paths (FileSystem only).
/// PlayableItems stores a JSON array of PlayableItem supporting multiple sources.
/// This migrator converts existing PlayableFilePaths data into PlayableItems format
/// for records that have PlayableFilePaths but no PlayableItems.
/// </summary>
public class V230Migrator : AbstractMigrator
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    public V230Migrator(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override string ApplyOnVersionEqualsOrBeforeString => "2.3.0-beta";

    protected override async Task MigrateAfterDbMigrationInternal(object? context)
    {
        await MigratePlayableFilesToPlayableItems();
    }

    private async Task MigratePlayableFilesToPlayableItems()
    {
        var dbCtx = GetRequiredService<BakabaseDbContext>();

        // Find all cache records that have PlayableFilePaths but no PlayableItems
        var cachesToMigrate = await dbCtx.ResourceCaches
            .Where(c => c.PlayableFilePaths != null && c.PlayableFilePaths != "" && (c.PlayableItems == null || c.PlayableItems == ""))
            .ToListAsync();

        if (cachesToMigrate.Count == 0)
        {
            Logger.LogInformation("No resource caches need PlayableFilePaths migration.");
            return;
        }

        Logger.LogInformation("Migrating {Count} resource cache records from PlayableFilePaths to PlayableItems.", cachesToMigrate.Count);

        var migrated = 0;
        foreach (var cache in cachesToMigrate)
        {
            try
            {
                var filePaths = cache.PlayableFilePaths!
                    .DeserializeAsStandardValue<List<string>>(StandardValueType.ListString);

                if (filePaths is { Count: > 0 })
                {
                    var playableItems = filePaths
                        .Where(p => !string.IsNullOrEmpty(p))
                        .Select(p => new PlayableItem
                        {
                            Source = ResourceSource.FileSystem,
                            Key = p
                        })
                        .ToList();

                    if (playableItems.Count > 0)
                    {
                        cache.PlayableItems = JsonSerializer.Serialize(playableItems, JsonOptions);
                        migrated++;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to migrate PlayableFilePaths for resource cache {ResourceId}.", cache.ResourceId);
            }
        }

        await dbCtx.SaveChangesAsync();
        Logger.LogInformation("Successfully migrated {Migrated}/{Total} resource cache records.", migrated, cachesToMigrate.Count);
    }
}
