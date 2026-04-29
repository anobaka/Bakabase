using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Migrations.V230;

/// <summary>
/// One-shot rewrite of persisted absolute AppData paths into AppData-relative form.
///
/// Velopack inserted a <c>current/</c> segment between the install root and AppData,
/// invalidating absolute paths captured before that change. After this migration
/// runs, all such paths live in DB as relative-to-AppData strings, so future
/// AppData root moves (further Velopack updates, manual cuts, custom DataPath)
/// resolve transparently via <see cref="IAppDataPathRelocator"/> at read time.
///
/// Implementation notes:
/// - Operates on <see cref="BakabaseDbContext"/> directly (not via
///   <see cref="FullMemoryCacheResourceService{TDbContext, TResource, TKey}"/>) because
///   not every affected table has a cache service registered for its concrete type
///   (e.g. <see cref="EnhancementDbModel"/> uses <c>ResourceService</c>, and
///   <see cref="CustomPropertyValueDbModel"/>'s cache layer is only bound through
///   the <c>ICustomPropertyValueService</c> interface).
/// - After committing the DB rewrites, we proactively evict the corresponding entries
///   from <see cref="GlobalCacheVault"/>. Today nothing populates these caches before
///   the migrator runs, but if that ordering ever changes we don't want stale,
///   pre-relocation paths sitting in memory; the next read re-loads from DB.
/// - Pure string transforms; no disk I/O against migrated paths is performed.
/// </summary>
public class PathsRelocationMigrator(IServiceProvider serviceProvider) : AbstractMigrator(serviceProvider)
{
    /// <summary>
    /// Threshold pinned just below the build height that ships this migration.
    /// Users on any 2.3.0-beta.&lt;=65 trigger the migrator on next launch; once their
    /// AppOptions.Version is bumped past 2.3.0-beta.65 by this run, the version gate
    /// in <see cref="AppHost"/> stops invoking it.
    /// </summary>
    protected override string ApplyOnVersionEqualsOrBeforeString => "2.3.0-beta.65";

    protected override async Task MigrateAfterDbMigrationInternal(object? context)
    {
        // Touching the relocator ensures the singleton is constructed, which installs it
        // into AppDataPaths so DB ↔ domain extension methods use the live instance.
        _ = GetRequiredService<IAppDataPathRelocator>();

        var dbCtx = GetRequiredService<BakabaseDbContext>();

        var totalRowsTouched = 0;

        totalRowsTouched += await Rewrite(
            "ReservedPropertyValue.CoverPaths",
            dbCtx.ReservedPropertyValues,
            r => r.CoverPaths,
            (r, v) => r.CoverPaths = v);

        totalRowsTouched += await Rewrite(
            "ResourceCache.CoverPaths",
            dbCtx.ResourceCaches,
            r => r.CoverPaths,
            (r, v) => r.CoverPaths = v);

        totalRowsTouched += await Rewrite(
            "ResourceSourceLink.LocalCoverPaths",
            dbCtx.ResourceSourceLinks,
            r => r.LocalCoverPaths,
            (r, v) => r.LocalCoverPaths = v);

        totalRowsTouched += await Rewrite(
            "CustomPropertyValue.Value",
            dbCtx.CustomPropertyValues,
            r => r.Value,
            (r, v) => r.Value = v);

        totalRowsTouched += await Rewrite(
            "Enhancement.Value",
            dbCtx.Enhancements,
            r => r.Value,
            (r, v) => r.Value = v);

        if (totalRowsTouched > 0)
        {
            await dbCtx.SaveChangesAsync();
        }

        // Defensive cache eviction. See class-level docstring.
        var vault = GetRequiredService<GlobalCacheVault>();
        vault.TryRemove(typeof(ReservedPropertyValue).FullName!, out _);
        vault.TryRemove(typeof(ResourceCacheDbModel).FullName!, out _);
        vault.TryRemove(typeof(ResourceSourceLinkDbModel).FullName!, out _);
        vault.TryRemove(typeof(CustomPropertyValueDbModel).FullName!, out _);
        vault.TryRemove(typeof(EnhancementDbModel).FullName!, out _);

        Logger.LogInformation("AppData path relocation: complete. {Rows} row(s) rewritten to relative form.",
            totalRowsTouched);
    }

    /// <summary>
    /// Loads every row in the table, rewrites the target ListString column entry-by-entry
    /// (resolve → relativize round trip; non-path tokens pass through), and lets EF
    /// change-tracking pick up modifications for the caller to commit via SaveChangesAsync.
    /// </summary>
    private async Task<int> Rewrite<TDbModel>(
        string label,
        DbSet<TDbModel> dbSet,
        Func<TDbModel, string?> getValue,
        Action<TDbModel, string?> setValue) where TDbModel : class
    {
        var all = await dbSet.ToListAsync();
        if (all.Count == 0) return 0;

        var changedCount = 0;
        foreach (var row in all)
        {
            var serialized = getValue(row);
            if (string.IsNullOrEmpty(serialized)) continue;

            var rewritten = TryRewriteSerializedListString(serialized);
            if (rewritten is null || rewritten == serialized) continue;

            setValue(row, rewritten);
            changedCount++;
        }

        if (changedCount > 0)
        {
            Logger.LogInformation("AppData path relocation: {Label} → {Count} row(s) rewritten.", label, changedCount);
        }

        return changedCount;
    }

    /// <summary>
    /// Parse a serialized ListString, push each entry through resolve→relativize, and
    /// reserialize. Returns the rewritten serialized string, or the original (or null) when
    /// no entry changed. Non-ListString content (numeric/datetime/etc.) deserializes to null
    /// and is left untouched.
    /// </summary>
    private static string? TryRewriteSerializedListString(string serialized)
    {
        var entries = serialized.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString);
        if (entries == null || entries.Count == 0) return serialized;

        var rewrittenEntries = entries
            .Select(e =>
            {
                var resolved = AppDataPaths.Relocator.Resolve(e);
                return AppDataPaths.Relocator.Relativize(resolved)!;
            })
            .ToList();

        if (rewrittenEntries.SequenceEqual(entries)) return serialized;

        return ((object)rewrittenEntries).SerializeAsStandardValue(StandardValueType.ListString);
    }
}
