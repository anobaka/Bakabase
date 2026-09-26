using System.Globalization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Merging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

/// <summary>
/// §13.3's noise budget (§9.4): a week of ordinary use on two desktops and a NAS they both sync with, every link
/// pulling hourly, over custom properties (package B's codec). An enhancer on PC-1 grows a tags property that ignores
/// case every night and tags PC-1's resources with what it adds — now and then a tag it already has in another casing,
/// which the service folds; people on either desktop rename a few tags or properties a day and tag a few resources;
/// once a week someone reorders the properties; twice a week someone deletes a tag no resource uses on any device.
/// That must ask each device at most one question in the whole week and delete nothing by itself: every tag nobody
/// deleted is on every device at the end, and every tag in use where it is used.
/// </summary>
[TestClass]
public class RealisticWeekTests
{
    private static readonly string[] Definitions = ["Genre", "Mood", "Studio", "Series"];
    private const string Kind = DataSyncKindIds.CustomProperty;

    private sealed class Week(int seed)
    {
        public readonly SimWorld World = new(seed);
        public readonly Random Random = new(seed);
        public SimNode Pc1 = null!, Pc2 = null!, Nas = null!;
        public SyncKey Tags;
        public int Created, Deleted, Renamed, Folded;
        public readonly List<string> Log = [];

        public IEnumerable<SimNode> Nodes => World.Nodes;
        public SimNode[] Desktops => [Pc1, Pc2];

        public SimRow TagsOn(SimNode node) => node.Rows.Single(r => r.IsLive && r.Keys.Contains(Tags));

        public static IReadOnlyList<CustomPropertyTagV1> TagList(SimRow row) => ((CustomPropertyContentV1)row.Content!).Tags;

        public static void SetTags(SimNode node, SimRow row, IEnumerable<CustomPropertyTagV1> tags) =>
            node.EditRow(row, (CustomPropertyContentV1)row.Content! with { Tags = tags.ToArray() });

        public void PullAll()
        {
            // Every hour each device pulls each of its links; who goes first varies.
            foreach (var node in Nodes.OrderBy(_ => Random.Next()).ToList())
            foreach (var link in node.Links.Values.OrderBy(l => l.Id).ToList())
                node.Pull(link);
        }

        public int UsageOn(SimNode node, string name)
        {
            var row = TagsOn(node);
            var tag = TagList(row).FirstOrDefault(t => t.Name == name);
            return tag is null ? 0 : node.Db.Usage.GetValueOrDefault((row.Kind, row.LocalKey))?.GetValueOrDefault(tag.Uuid!) ?? 0;
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public void AWeekOfOrdinaryUseAsksAtMostOncePerDeviceAndDeletesNothingByItself(int seed)
    {
        var w = new Week(seed);
        w.Pc1 = w.World.AddNode("PC-1");
        w.Pc2 = w.World.AddNode("PC-2");
        w.Nas = w.World.AddNode("NAS", headless: true);
        foreach (var pc in w.Desktops)
        {
            pc.Follow(w.Nas);
            w.Nas.Follow(pc);
        }

        // PC-1 holds the library; the others receive it at their first contact.
        var tags = w.Pc1.Create(Kind, new CustomPropertyContentV1
        {
            Name = "Tags", Type = PropertyType.Tags, IgnoreCase = true,
            Tags = Enumerable.Range(1, 12).Select(i => new CustomPropertyTagV1(w.World.NewChildId(), i % 3 == 0 ? "Studio" : null,
                Label(i), null)).ToArray(),
        });
        w.Created = 12;
        foreach (var name in Definitions)
        {
            w.Pc1.Create(Kind, new CustomPropertyContentV1
            {
                Name = name, Type = PropertyType.MultipleChoice, IgnoreCase = false,
                Choices = Enumerable.Range(0, 3)
                    .Select(i => new CustomPropertyChoiceV1(w.World.NewChildId(), name + " " + i, "#0090ff")).ToArray(),
            });
        }

        for (var i = 0; i < 3; i++) w.PullAll();
        w.Tags = tags.Primary;
        foreach (var tag in Week.TagList(tags).Take(8)) w.Pc1.UseChild(tags, tag.Uuid!, 1 + w.Random.Next(20));

        for (var day = 0; day < 7; day++)
        for (var hour = 0; hour < 24; hour++)
        {
            w.World.Clock.Advance(TimeSpan.FromHours(1));
            Hour(w, day, hour);
            w.PullAll();
        }

        for (var i = 0; i < 3; i++) w.PullAll();

        var report = $"seed {seed}:\n  " + string.Join("\n  ", w.Log) + "\n" +
                     string.Concat(w.Nodes.Select(SimDigest.Describe));
        foreach (var node in w.Nodes) Assert.AreEqual(0, node.Violations.Count, string.Join("\n", node.Violations) + report);

        // At most one question per device for the whole week.
        foreach (var pc in w.Desktops)
        {
            var prompts = pc.Notifications.Count(n => n.IsPrompt);
            Assert.IsTrue(prompts <= 1, $"{pc} was asked {prompts} times: " +
                                        string.Join(", ", pc.Items.Select(i => i.ToString())) + report);
        }

        Assert.IsTrue(w.Nas.Items.Count <= 1, $"the NAS waits on {w.Nas.Items.Count} decisions" + report);

        // Nothing deleted by itself: every tag nobody deleted is everywhere, and a tag in use is still there.
        var expected = w.Created - w.Deleted;
        foreach (var node in w.Nodes)
        {
            Assert.AreEqual(expected, Week.TagList(w.TagsOn(node)).Count, $"{node} holds another set of tags" + report);
            Assert.AreEqual(0, node.OpenDecisions + node.Links.Values.Count(l => l.Paused is not null),
                $"{node} still waits" + report);
        }

        var names = Week.TagList(w.TagsOn(w.Pc1)).Select(t => t.Group + "/" + t.Name).OrderBy(l => l, StringComparer.Ordinal).ToList();
        foreach (var node in w.Nodes)
        {
            CollectionAssert.AreEqual(names,
                Week.TagList(w.TagsOn(node)).Select(t => t.Group + "/" + t.Name).OrderBy(l => l, StringComparer.Ordinal).ToList(),
                $"{node}'s tags differ" + report);
        }

        Assert.IsTrue(w.Created >= 12 + 7 * 2, "the enhancer ran every night");
        Assert.IsTrue(w.Renamed >= 7 * 2, "the week renamed a few tags a day");
        Assert.IsTrue(w.Deleted >= 1, "the week deleted unused tags");
        Assert.IsTrue(w.Folded >= 1, "the service folded a tag the enhancer found in another casing");
    }

    private static string Label(int n) => "tag " + n.ToString(CultureInfo.InvariantCulture);

    private static void Hour(Week w, int day, int hour)
    {
        var random = w.Random;

        // The enhancer runs at night on PC-1: a few new tags, used by the resources it enhanced. Now and then it finds
        // a tag PC-1 already has in another casing; the service folds it into that one (IgnoreCase, F72).
        if (hour is 2 or 4)
        {
            var row = w.TagsOn(w.Pc1);
            var existing = Week.TagList(row);
            var added = Enumerable.Range(0, 1 + random.Next(3)).Select(i =>
            {
                // The first night's first tag is always one it has already, so every week folds at least once.
                if ((random.Next(4) == 0 || (day == 0 && hour == 2 && i == 0)) && existing.Count > 0)
                {
                    var twin = existing[random.Next(existing.Count)];
                    return new CustomPropertyTagV1(w.World.NewChildId(), twin.Group, twin.Name.ToUpperInvariant(), null);
                }

                return new CustomPropertyTagV1(w.World.NewChildId(), null, Label(++w.Created), null);
            }).ToList();
            Week.SetTags(w.Pc1, row, [.. existing, .. added]);
            var stored = Week.TagList(row).Select(t => t.Uuid).ToHashSet(StringComparer.Ordinal);
            var kept = added.Where(t => stored.Contains(t.Uuid)).ToList();
            w.Folded += added.Count - kept.Count;
            foreach (var tag in kept) w.Pc1.UseChild(row, tag.Uuid!, 1 + random.Next(10));
            w.Log.Add($"day {day} {hour:00}h: PC-1's enhancer adds {string.Join(", ", added.Select(t => t.Name))}" +
                      (kept.Count < added.Count ? $" ({added.Count - kept.Count} folded)" : ""));
        }

        // A few renames a day, in the daytime, on either desktop: mostly tags, sometimes a property.
        if (hour is >= 9 and <= 21 && random.Next(4) == 0)
        {
            var pc = w.Desktops[random.Next(2)];
            if (random.Next(5) == 0)
            {
                var row = pc.Rows.Where(r => r.IsLive && !r.Keys.Contains(w.Tags)).OrderBy(r => r.LocalKey).ToList() is { Count: > 0 } rows
                    ? rows[random.Next(rows.Count)]
                    : null;
                if (row is not null)
                {
                    var name = Definitions[random.Next(Definitions.Length)] + " " + (++w.Renamed).ToString(CultureInfo.InvariantCulture);
                    w.Log.Add($"day {day} {hour:00}h: {pc} renames {row.Name} to {name}");
                    pc.EditRow(row, SimKinds.Of(row.Kind).Renamed(row.Content!, name));
                }
            }
            else
            {
                var row = w.TagsOn(pc);
                var list = Week.TagList(row).ToList();
                var at = random.Next(list.Count);
                var name = list[at].Name.Split(" (")[0] + " (" + (++w.Renamed).ToString(CultureInfo.InvariantCulture) + ")";
                w.Log.Add($"day {day} {hour:00}h: {pc} renames {list[at].Name} to {name}");
                list[at] = list[at] with { Name = name };
                Week.SetTags(pc, row, list);
            }
        }

        // People tag a few resources by hand on either desktop.
        if (hour is >= 9 and <= 21 && random.Next(3) == 0)
        {
            var pc = w.Desktops[random.Next(2)];
            var row = w.TagsOn(pc);
            var list = Week.TagList(row);
            var tag = list[random.Next(list.Count)];
            var usage = pc.Db.Usage.GetValueOrDefault((row.Kind, row.LocalKey))?.GetValueOrDefault(tag.Uuid!) ?? 0;
            pc.UseChild(row, tag.Uuid!, usage + 1);
        }

        // The weekly reorder.
        if (day == 3 && hour == 11)
        {
            var row = w.Pc2.Live(Kind).Last();
            w.Log.Add($"day {day} {hour:00}h: PC-2 moves {row.Name} to the top");
            w.Pc2.Move(row, 0);
        }

        // Twice a week someone deletes a tag no resource uses on any device.
        if (day is 2 or 5 && hour == 8)
        {
            var pc = w.Desktops[day == 2 ? 1 : 0];
            var row = w.TagsOn(pc);
            var unused = Week.TagList(row).Where(t => w.Nodes.All(n => w.UsageOn(n, t.Name) == 0)).ToList();
            if (unused.Count > 0)
            {
                var gone = unused[random.Next(unused.Count)];
                w.Log.Add($"day {day} {hour:00}h: {pc} deletes the unused {gone.Name}");
                Week.SetTags(pc, row, Week.TagList(row).Where(t => t.Uuid != gone.Uuid));
                w.Deleted++;
            }
        }
    }
}
