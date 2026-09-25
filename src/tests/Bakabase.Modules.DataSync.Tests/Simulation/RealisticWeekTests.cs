using System.Globalization;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

/// <summary>
/// §13.3's noise budget (§9.4): a week of ordinary use on two desktops and a NAS they both sync with, every link
/// pulling hourly. An enhancer on PC-1 grows a tags definition every night and tags PC-1's resources with what it
/// adds; people on either desktop rename a few options or definitions a day and tag a few resources; once a week
/// someone reorders the definitions; twice a week someone deletes an option no resource uses on any device. That
/// must ask each device at most one question in the whole week and delete nothing by itself: every option nobody
/// deleted is on every device at the end, and every option in use where it is used.
/// </summary>
[TestClass]
public class RealisticWeekTests
{
    private static readonly string[] Definitions = ["Genre", "Mood", "Studio", "Series"];

    private sealed class Week(int seed)
    {
        public readonly SimWorld World = new(seed);
        public readonly Random Random = new(seed);
        public SimNode Pc1 = null!, Pc2 = null!, Nas = null!;
        public SyncKey Tags;
        public int Created, Deleted, Renamed;
        public readonly List<string> Log = [];

        public IEnumerable<SimNode> Nodes => World.Nodes;
        public SimNode[] Desktops => [Pc1, Pc2];

        public SimRow TagsOn(SimNode node) => node.Rows.Single(r => r.IsLive && r.Keys.Contains(Tags));

        public void PullAll()
        {
            // Every hour each device pulls each of its links; who goes first varies.
            foreach (var node in Nodes.OrderBy(_ => Random.Next()).ToList())
            foreach (var link in node.Links.Values.OrderBy(l => l.Id).ToList())
                node.Pull(link);
        }

        public int UsageOn(SimNode node, string label)
        {
            var row = TagsOn(node);
            var child = row.Item!.Children.FirstOrDefault(c => c.Label == label);
            return child is null ? 0 : node.Db.Usage.GetValueOrDefault((row.Kind, row.LocalKey))?.GetValueOrDefault(child.Id) ?? 0;
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
        var tags = w.Pc1.Create(new TestItemContent("Tags", null,
            Enumerable.Range(1, 12).Select(i => new TestChild(w.World.NewChildId(), Label(i))), "Tags"));
        w.Created = 12;
        foreach (var name in Definitions)
        {
            w.Pc1.Create(new TestItemContent(name, "#0090ff",
                Enumerable.Range(0, 3).Select(i => new TestChild(w.World.NewChildId(), name + " " + i)), "Choice"));
        }

        for (var i = 0; i < 3; i++) w.PullAll();
        w.Tags = tags.Primary;
        foreach (var child in tags.Item!.Children.Take(8)) w.Pc1.UseChild(tags, child.Id, 1 + w.Random.Next(20));

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

        // Nothing deleted by itself: every option nobody deleted is everywhere, and an option in use is still there.
        var expected = w.Created - w.Deleted;
        foreach (var node in w.Nodes)
        {
            Assert.AreEqual(expected, w.TagsOn(node).Item!.Children.Count, $"{node} holds another set of options" + report);
            Assert.AreEqual(0, node.OpenDecisions + node.Links.Values.Count(l => l.Paused is not null),
                $"{node} still waits" + report);
        }

        var labels = w.TagsOn(w.Pc1).Item!.Children.Select(c => c.Label).OrderBy(l => l, StringComparer.Ordinal).ToList();
        foreach (var node in w.Nodes)
        {
            CollectionAssert.AreEqual(labels,
                w.TagsOn(node).Item!.Children.Select(c => c.Label).OrderBy(l => l, StringComparer.Ordinal).ToList(),
                $"{node}'s options differ" + report);
        }

        Assert.IsTrue(w.Created >= 12 + 7 * 2, "the enhancer ran every night");
        Assert.IsTrue(w.Renamed >= 7 * 2, "the week renamed a few options a day");
        Assert.IsTrue(w.Deleted >= 1, "the week deleted unused options");
    }

    private static string Label(int n) => "tag " + n.ToString(CultureInfo.InvariantCulture);

    private static void Hour(Week w, int day, int hour)
    {
        var random = w.Random;

        // The enhancer runs at night on PC-1: a few new tags, used by the resources it enhanced.
        if (hour is 2 or 4)
        {
            var row = w.TagsOn(w.Pc1);
            var added = Enumerable.Range(0, 1 + random.Next(3))
                .Select(_ => new TestChild(w.World.NewChildId(), Label(++w.Created))).ToList();
            w.Pc1.EditRow(row, row.Item!.With(children: [.. row.Item!.Children, .. added]));
            foreach (var child in added) w.Pc1.UseChild(row, child.Id, 1 + random.Next(10));
            w.Log.Add($"day {day} {hour:00}h: PC-1's enhancer adds {string.Join(", ", added.Select(c => c.Label))}");
        }

        // A few renames a day, in the daytime, on either desktop: mostly options, sometimes a definition.
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
                var children = row.Item!.Children.ToList();
                var at = random.Next(children.Count);
                var label = children[at].Label.Split(" (")[0] + " (" + (++w.Renamed).ToString(CultureInfo.InvariantCulture) + ")";
                w.Log.Add($"day {day} {hour:00}h: {pc} renames {children[at].Label} to {label}");
                children[at] = children[at] with { Label = label };
                pc.EditRow(row, row.Item!.With(children: children));
            }
        }

        // People tag a few resources by hand on either desktop.
        if (hour is >= 9 and <= 21 && random.Next(3) == 0)
        {
            var pc = w.Desktops[random.Next(2)];
            var row = w.TagsOn(pc);
            var child = row.Item!.Children[random.Next(row.Item!.Children.Count)];
            var usage = pc.Db.Usage.GetValueOrDefault((row.Kind, row.LocalKey))?.GetValueOrDefault(child.Id) ?? 0;
            pc.UseChild(row, child.Id, usage + 1);
        }

        // The weekly reorder.
        if (day == 3 && hour == 11)
        {
            var row = w.Pc2.Live(TestItemCodec.Kind).Last();
            w.Log.Add($"day {day} {hour:00}h: PC-2 moves {row.Name} to the top");
            w.Pc2.Move(row, 0);
        }

        // Twice a week someone deletes an option no resource uses on any device.
        if (day is 2 or 5 && hour == 8)
        {
            var pc = w.Desktops[day == 2 ? 1 : 0];
            var row = w.TagsOn(pc);
            var unused = row.Item!.Children.Where(c => w.Nodes.All(n => w.UsageOn(n, c.Label) == 0)).ToList();
            if (unused.Count > 0)
            {
                var gone = unused[random.Next(unused.Count)];
                w.Log.Add($"day {day} {hour:00}h: {pc} deletes the unused {gone.Label}");
                pc.EditRow(row, row.Item!.With(children: row.Item!.Children.Where(c => c.Id != gone.Id).ToList()));
                w.Deleted++;
            }
        }
    }
}
