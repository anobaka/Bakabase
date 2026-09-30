using System.Globalization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.DataSync.Convergence;

/// <summary>
/// §13.3's noise budget (§9.4) on the real stack: a week of ordinary use on two desktops and a NAS they both keep in
/// step with, every host pulling every two hours. An enhancer on PC-1 grows a tags property that ignores case every
/// night and tags PC-1's resources with what it adds — now and then a tag it already has in another casing, which the
/// service folds; people on either desktop rename a few tags or properties a day and tag a few resources; once a week
/// someone reorders the properties; twice a week someone deletes a tag no resource uses on any device. That asks each
/// desktop at most one question in the whole week (answered at its end) and deletes nothing by itself: every tag
/// nobody deleted is on every host at the end. <c>DATASYNC_WEEK_SEED</c> runs another week.
/// </summary>
[TestClass]
public class RealisticWeekTests
{
    private static readonly string[] Definitions = ["Genre", "Mood", "Studio", "Series"];
    private static readonly string[] Prompts =
        [DataSyncNotifier.NeedsYouCase, DataSyncNotifier.PausedCase, DataSyncNotifier.RestoreCase];

    [TestMethod]
    [Timeout(300_000)]
    public async Task A_week_of_ordinary_use_asks_at_most_once_per_device_and_deletes_nothing_by_itself()
    {
        var seed = int.TryParse(Environment.GetEnvironmentVariable("DATASYNC_WEEK_SEED"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var s) ? s : 1;
        var random = new Random(seed);
        var world = new SyncWorld();
        var nas = await world.AddHostAsync("NAS", headless: true);
        var pc1 = await world.AddHostAsync("PC-1");
        var pc2 = await world.AddHostAsync("PC-2");
        SyncHost[] desktops = [pc1, pc2];
        await world.LinkAsync(pc1, nas, DataSyncLinkMode.TwoWay);
        await world.LinkAsync(pc2, nas, DataSyncLinkMode.TwoWay);

        // PC-1 holds the library; the others take it from their pulls.
        var created = 12;
        var tags = await pc1.AddPropertyAsync("Tags", PropertyType.Tags,
            Enumerable.Range(1, created).Select(Label).ToArray());
        await pc1.PutAsync(tags, "Tags", true,
            SyncOptions.Read(tags).Select((t, i) => i % 3 == 2 ? t with { Group = "Studio" } : t).ToList());
        foreach (var name in Definitions)
            await pc1.AddPropertyAsync(name, PropertyType.MultipleChoice, name + " 0", name + " 1", name + " 2");
        for (var i = 0; i < 3; i++) await world.SyncAllAsync();
        foreach (var tag in SyncOptions.Read(await pc1.PropertyAsync("Tags")).Take(8))
            await pc1.UseAsync(await pc1.PropertyAsync("Tags"), tag.Id, 1 + random.Next(20));

        var before = new Dictionary<SyncHost, int>();
        foreach (var pc in desktops) before[pc] = await PromptsAsync(pc);
        int renamed = 0, folded = 0;
        var deletedIds = new HashSet<string>();
        var log = new List<string>();
        for (var day = 0; day < 7; day++)
        for (var hour = 0; hour < 24; hour++)
        {
            world.Clock.Advance(TimeSpan.FromHours(1));

            // The enhancer runs at night on PC-1: a few new tags its resources use. Now and then one PC-1 already has in
            // another casing, which the service folds into that one (IgnoreCase, F72).
            if (hour is 2 or 4)
            {
                var existing = SyncOptions.Read(await pc1.PropertyAsync("Tags"));
                SyncOption Twin(SyncOption of) => new(SyncHost.NewId(), of.Label.ToUpperInvariant(), null, of.Group);
                var added = Enumerable.Range(0, 1 + random.Next(3)).Select(i =>
                    (random.Next(4) == 0 || (day == 0 && hour == 2 && i == 0)) && existing.Count > 0
                        ? Twin(existing[random.Next(existing.Count)])
                        : new SyncOption(SyncHost.NewId(), Label(++created))).ToList();
                await pc1.EditAsync("Tags", o => o.Concat(added));
                var stored = SyncOptions.Read(await pc1.PropertyAsync("Tags")).Select(t => t.Id).ToHashSet();
                var kept = added.Where(t => stored.Contains(t.Id)).ToList();
                folded += added.Count - kept.Count;
                foreach (var tag in kept) await pc1.UseAsync(await pc1.PropertyAsync("Tags"), tag.Id, 1 + random.Next(10));
                log.Add($"day {day} {hour:00}h: PC-1's enhancer adds {string.Join(", ", added.Select(t => t.Label))}");
            }

            // A few renames a day on either desktop: mostly tags, sometimes a property; people tag a few resources.
            if (hour is >= 9 and <= 21 && random.Next(4) == 0)
            {
                var pc = desktops[random.Next(2)];
                if (random.Next(5) == 0)
                {
                    var property = (await pc.PropertiesAsync()).Where(p => p.Name != "Tags").OrderBy(p => p.Id).ToList()
                        is { Count: > 0 } list ? list[random.Next(list.Count)] : null;
                    if (property is not null)
                    {
                        var name = Definitions[random.Next(Definitions.Length)] + " " + ++renamed;
                        await pc.RenameAsync(property.Name, name);
                        log.Add($"day {day} {hour:00}h: {pc} renames {property.Name} to {name}");
                    }
                }
                else
                {
                    var list = SyncOptions.Read(await pc.PropertyAsync("Tags"));
                    var at = random.Next(list.Count);
                    var name = list[at].Label.Split(" (")[0] + " (" + ++renamed + ")";
                    await pc.EditAsync("Tags", o => o.Select((t, i) => i == at ? t with { Label = name } : t));
                    log.Add($"day {day} {hour:00}h: {pc} renames {list[at].Label} to {name}");
                }
            }

            if (hour is >= 9 and <= 21 && random.Next(3) == 0)
            {
                var pc = desktops[random.Next(2)];
                var property = await pc.PropertyAsync("Tags");
                var list = SyncOptions.Read(property);
                await pc.UseAsync(property, list[random.Next(list.Count)].Id);
            }

            // The weekly reorder, and twice a week a tag no resource uses on any host goes.
            if (day == 3 && hour == 11)
            {
                var ids = (await pc2.PropertiesAsync()).Select(p => p.Id).ToList();
                await pc2.Node.InScopeAsync(async sp =>
                {
                    await sp.GetRequiredService<Bakabase.Modules.Property.Abstractions.Services.ICustomPropertyService>()
                        .Sort([ids[^1], .. ids[..^1]]);
                    return 0;
                });
                log.Add($"day {day} {hour:00}h: PC-2 moves its last property to the top");
            }

            if (day is 2 or 5 && hour == 8)
            {
                var pc = desktops[day == 2 ? 1 : 0];
                var used = new HashSet<string>();
                foreach (var host in world.Hosts)
                {
                    var id = (await host.PropertyAsync("Tags")).Id;
                    if ((await host.UsageAsync()).TryGetValue(id, out var ids)) used.UnionWith(ids);
                }

                var unused = SyncOptions.Read(await pc.PropertyAsync("Tags")).Where(t => !used.Contains(t.Id)).ToList();
                if (unused.Count > 0)
                {
                    var gone = unused[random.Next(unused.Count)];
                    await pc.EditAsync("Tags", o => o.Where(t => t.Id != gone.Id));
                    deletedIds.Add(gone.Id);
                    log.Add($"day {day} {hour:00}h: {pc} deletes the unused {gone.Label}");
                }
            }

            if (hour % 2 == 1) await world.SyncAllAsync();
        }

        for (var i = 0; i < 3; i++) await world.SyncAllAsync();

        var report = $"seed {seed}:\n  " + string.Join("\n  ", log) + "\n" + await world.DigestAsync(true);
        foreach (var pc in desktops)
        {
            var asked = await PromptsAsync(pc) - before[pc];
            Assert.IsTrue(asked <= 1, $"{pc} was asked {asked} times" + report);
        }

        Assert.IsTrue((await nas.OpenItemsAsync()).Count <= 1, "the NAS waits on more than one decision" + report);

        // What the week asked (two renames of one tag between pulls), the person answers on the desktop: keep this one's.
        foreach (var pc in desktops)
        {
            foreach (var item in await pc.OpenItemsAsync())
            {
                var action = item.AllowedActions.Contains(DataSyncInboxAction.KeepLocal)
                    ? DataSyncInboxAction.KeepLocal
                    : item.AllowedActions[0];
                Assert.IsNull(await pc.ResolveAsync(item, action), $"{pc} answers {item.Type}" + report);
                log.Add($"then {pc} answers {item.Type} of {item.Payload.EntityName} with {action}");
            }
        }

        for (var i = 0; i < 3; i++) await world.SyncAllAsync();
        report = $"seed {seed}:\n  " + string.Join("\n  ", log) + "\n" + await world.DigestAsync(true);

        // Nothing deleted by itself: every tag nobody deleted is everywhere, the same everywhere. A tag the other desktop
        // renamed before it pulled the deletion stays, renamed (an edit wins over a deletion, §8.5.4).
        var final = SyncOptions.Read(await pc1.PropertyAsync("Tags"));
        var editWon = deletedIds.Count(id => final.Any(t => t.Id == id));
        var expected = final.Select(t => t.Group + "/" + t.Label).Order(StringComparer.Ordinal).ToList();
        Assert.AreEqual(created - deletedIds.Count + editWon, expected.Count, "PC-1 holds another set of tags" + report);
        foreach (var host in world.Hosts)
        {
            CollectionAssert.AreEqual(expected, SyncOptions.Read(await host.PropertyAsync("Tags"))
                .Select(t => t.Group + "/" + t.Label).Order(StringComparer.Ordinal).ToList(), $"{host}'s tags" + report);
            Assert.AreEqual(0, (await host.OpenItemsAsync()).Count, $"{host} still waits" + report);
        }

        // CI's week does all of it; another seed's may find no unused tag to delete.
        if (seed == 1)
        {
            Assert.IsTrue(renamed >= 7 && deletedIds.Count > editWon && folded >= 1,
                $"the week renamed {renamed}, deleted {deletedIds.Count - editWon} and folded {folded}" + report);
        }
    }

    private static string Label(int n) => "tag " + n.ToString(CultureInfo.InvariantCulture);

    /// <summary>The notifications that ask something of the person (§9.4), as the host's center keeps them.</summary>
    private static Task<int> PromptsAsync(SyncHost host) => host.Node.InScopeAsync(async sp =>
    {
        var found = await sp.GetRequiredService<INotificationService>()
            .SearchAsync(new NotificationSearchInputModel { PageSize = 500 });
        return (found.Data ?? []).Count(n => Prompts.Any(p => n.PayloadJson?.Contains($"\"case\":\"{p}\"") == true));
    });
}
