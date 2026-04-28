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
/// Operates through each table's <see cref="FullMemoryCacheResourceService{TDbContext, TResource, TKey}"/>
/// (the cache layer used by higher-level domain services) so cache state stays
/// consistent with the underlying DB rows. Pure string transforms; no disk I/O
/// against migrated paths is performed (no <c>File.Exists</c> checks).
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

        var totalRowsTouched = 0;

        totalRowsTouched += await Rewrite(
            "ReservedPropertyValue.CoverPaths",
            GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ReservedPropertyValue, int>>(),
            r => r.CoverPaths,
            (r, v) => r.CoverPaths = v);

        totalRowsTouched += await Rewrite(
            "ResourceCache.CoverPaths",
            GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int>>(),
            r => r.CoverPaths,
            (r, v) => r.CoverPaths = v);

        totalRowsTouched += await Rewrite(
            "ResourceSourceLink.LocalCoverPaths",
            GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceSourceLinkDbModel, int>>(),
            r => r.LocalCoverPaths,
            (r, v) => r.LocalCoverPaths = v);

        totalRowsTouched += await Rewrite(
            "CustomPropertyValue.Value",
            GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, CustomPropertyValueDbModel, int>>(),
            r => r.Value,
            (r, v) => r.Value = v);

        totalRowsTouched += await Rewrite(
            "Enhancement.Value",
            GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, EnhancementDbModel, int>>(),
            r => r.Value,
            (r, v) => r.Value = v);

        Logger.LogInformation("AppData path relocation: complete. {Rows} row(s) rewritten to relative form.",
            totalRowsTouched);
    }

    /// <summary>
    /// Loads every row via the cache ORM, rewrites the target ListString column entry-by-entry
    /// (resolve → relativize round trip; non-path tokens pass through), and writes only the
    /// rows that actually changed.
    /// </summary>
    private async Task<int> Rewrite<TDbModel>(
        string label,
        FullMemoryCacheResourceService<BakabaseDbContext, TDbModel, int> orm,
        Func<TDbModel, string?> getValue,
        Action<TDbModel, string?> setValue) where TDbModel : class
    {
        var all = await orm.GetAll();
        if (all.Count == 0) return 0;

        var changed = new List<TDbModel>();
        foreach (var row in all)
        {
            var serialized = getValue(row);
            if (string.IsNullOrEmpty(serialized)) continue;

            var rewritten = TryRewriteSerializedListString(serialized);
            if (rewritten is null || rewritten == serialized) continue;

            setValue(row, rewritten);
            changed.Add(row);
        }

        if (changed.Count > 0)
        {
            await orm.UpdateRange(changed);
            Logger.LogInformation("AppData path relocation: {Label} → {Count} row(s) rewritten.", label, changed.Count);
        }

        return changed.Count;
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
