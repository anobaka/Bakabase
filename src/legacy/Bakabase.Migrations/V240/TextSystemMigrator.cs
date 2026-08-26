using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.InsideWorld.Business;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Migrations.V240;

/// <summary>
/// One-shot copy of the legacy <c>SpecialTexts</c> rows into the unified text vocabulary
/// (<c>TextTypes</c> + <c>TextEntries</c>), where builtin and user-defined types share one table
/// and one id space.
///
/// Implementation notes:
/// - The old table and model are deliberately kept (as
///   <see cref="Bakabase.InsideWorld.Business.Models.Db.LegacySpecialText"/>). App migrators run
///   *after* EF schema migrations, so a migration dropping <c>SpecialTexts</c> would delete the
///   source before this ever reads it whenever a user upgrades across the release that introduced
///   the copy. Leaving the table in place as a dormant relic costs two small columns and removes
///   that hazard.
/// - Re-entrant: the gate is the global AppOptions version, so a crash mid-copy means a rerun.
///   Entries already present under their target type (matched on both values) are skipped, which
///   also keeps this consistent with how prefabs are topped up.
/// - Reads <see cref="BakabaseDbContext"/> directly rather than through a cache service, matching
///   <c>PathsRelocationMigrator</c>: the legacy rows are on their way out and need no caching.
/// </summary>
public class TextSystemMigrator(IServiceProvider serviceProvider) : AbstractMigrator(serviceProvider)
{
    /// <summary>
    /// Must sit at or above the build height that ships the unified text vocabulary — a threshold
    /// below it never fires, since the gate asks whether the installed version is older. Landing a
    /// little high only costs a few no-op startups: the copy skips entries that already exist.
    /// </summary>
    protected override string ApplyOnVersionEqualsOrBeforeString => "2.4.0-beta.99";

    protected override async Task MigrateAfterDbMigrationInternal(object? context)
    {
        var logger = GetRequiredService<ILoggerFactory>().CreateLogger<TextSystemMigrator>();
        var vocabulary = GetRequiredService<ITextVocabularyService>();

        // Only the type rows: prefab entries are the user's data, and the rows being copied below
        // already are whichever prefabs they kept.
        await vocabulary.EnsureBuiltinTypes();

        var dbCtx = GetRequiredService<BakabaseDbContext>();
        var legacyRows = await dbCtx.SpecialTexts.AsNoTracking().ToListAsync();
        if (legacyRows.Count == 0)
        {
            logger.LogInformation("No legacy special texts to migrate");
            return;
        }

        var migrated = 0;
        foreach (var group in legacyRows.GroupBy(r => r.Type))
        {
            var wellKnown = MapType(group.Key);
            if (wellKnown == null)
            {
                logger.LogWarning("Skipped {Count} legacy special text(s) of unknown type {Type}",
                    group.Count(), group.Key);
                continue;
            }

            var typeId = await vocabulary.GetTypeId(wellKnown.Value);
            var existing = (await vocabulary.GetEntries(typeId))
                .Select(e => (e.Value1, e.Value2))
                .ToHashSet();

            // Value2 is carried over as-is even for shapes that only read the first value (Volume
            // rows hold an ordinal no consumer reads today) — migrating must not decide to drop data.
            var missing = group
                .Select(r => (r.Value1, r.Value2))
                .Where(v => !existing.Contains(v))
                .Distinct()
                .ToList();

            if (missing.Count > 0)
            {
                await vocabulary.AddEntries(typeId, missing);
                migrated += missing.Count;
            }
        }

        logger.LogInformation("Migrated {Migrated} legacy special text(s) into the text vocabulary", migrated);
    }

    /// <summary>
    /// The new handles reuse the legacy enum's values, so this is a straight cast guarded against
    /// rows carrying a value no longer defined.
    /// </summary>
    private static WellKnownTextType? MapType(int type) =>
        Enum.IsDefined(typeof(WellKnownTextType), type) ? (WellKnownTextType) type : null;
}
