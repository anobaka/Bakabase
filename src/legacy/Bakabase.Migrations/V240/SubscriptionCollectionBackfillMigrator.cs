using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Models.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Migrations.V240;

/// <summary>
/// Gives every subscription a collection to fill.
/// <para>
/// A subscription used to keep a rolling snapshot of what its source last listed — strings in a
/// table nobody could act on. It now fills a collection with real resources, which means every
/// existing subscription needs one; the honest default is a collection of its own name, so the
/// upgrade produces exactly what the user would have made by hand.
/// </para>
/// <para>
/// Re-entrant: a subscription that already has a collection is left alone, so a crash halfway
/// through means a rerun rather than a second set of collections.
/// </para>
/// </summary>
public class SubscriptionCollectionBackfillMigrator(IServiceProvider serviceProvider)
    : AbstractMigrator(serviceProvider)
{
    /// <summary>
    /// At or above the build that ships collections as subscription targets — a threshold below it
    /// never fires, since the gate asks whether the installed version is older.
    /// </summary>
    protected override string ApplyOnVersionEqualsOrBeforeString => "2.4.0-beta.99";

    protected override async Task MigrateAfterDbMigrationInternal(object? context)
    {
        var logger = GetRequiredService<ILoggerFactory>()
            .CreateLogger<SubscriptionCollectionBackfillMigrator>();
        var db = GetRequiredService<BakabaseDbContext>();

        var orphans = await db.Subscriptions.Where(s => s.CollectionId == null).ToListAsync();

        if (orphans.Count == 0)
        {
            logger.LogInformation("Every subscription already fills a collection");

            return;
        }

        var collections = GetRequiredService<ICollectionService>();

        foreach (var subscription in orphans)
        {
            var collection = await collections.Add(new CollectionInputModel
            {
                Name = subscription.DisplayName,
                Description = $"Filled by the {subscription.Kind} subscription.",
            });

            subscription.CollectionId = collection.Id;
        }

        await db.SaveChangesAsync();

        logger.LogInformation("Gave {Count} subscription(s) a collection of their own name",
            orphans.Count);
    }
}
