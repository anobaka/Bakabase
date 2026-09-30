using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Services;

namespace Bakabase.Tests.DataSync.Convergence;

/// <summary>
/// Two real hosts: Convert beside a rename made on the other device (§8.5.6), and a copy once (§8.1, §8.3) — the peer's
/// values it takes, the name matches its preview shows on a device nobody read, and one its preview could not show.
/// </summary>
[TestClass]
public class ConvertAndCopyOnceTests
{
    [TestMethod]
    [DataRow(DataSyncInboxAction.UseRemote, "Genre A")]
    [DataRow(DataSyncInboxAction.KeepLocal, "Genre B")]
    public async Task Convert_beside_a_concurrent_rename_converges_once_the_converting_device_decides_the_name(
        DataSyncInboxAction decision, string expected)
    {
        var world = new SyncWorld();
        var a = await world.AddHostAsync("A");
        var b = await world.AddHostAsync("B");
        var genre = await a.AddPropertyAsync("Genre", PropertyType.SingleChoice, "Rock", "Jazz", "Pop");
        await world.LinkAsync(b, a, DataSyncLinkMode.TwoWay);
        var ids = SyncOptions.Read(genre).ToDictionary(o => o.Label, o => o.Id);
        await a.UseAsync(genre, ids["Rock"]);
        await a.UseAsync(genre, ids["Jazz"]);
        await b.UseAsync(await b.PropertyAsync("Genre"), ids["Rock"]);
        await b.UseAsync(await b.PropertyAsync("Genre"), ids["Pop"]);

        // A makes it multiple choice (its options rebuilt from its values) and renames it; B renames it meanwhile.
        await a.ChangeTypeAsync(genre, PropertyType.MultipleChoice);
        await a.RenameAsync("Genre", "Genre A");
        await b.RenameAsync("Genre", "Genre B");
        await world.SyncAsync(b);
        var typeChange = (await b.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.TypeChange);
        Assert.IsNull(await b.ResolveAsync(typeChange, DataSyncInboxAction.Convert));

        // The name waits for a person on B. Until then B withholds its half-converted definition, so A has nothing of
        // it to merge and nothing to decide.
        await world.SyncAsync(a);
        Assert.AreEqual(0, (await a.OpenItemsAsync()).Count);
        Assert.AreEqual(("Genre A", PropertyType.MultipleChoice),
            ((await a.PropertiesAsync()).Single().Name, (await a.PropertiesAsync()).Single().Type));
        var name = (await b.OpenItemsAsync()).Single(i => i.SubjectPath == "name");
        Assert.IsNull(await b.ResolveAsync(name, decision));
        for (var round = 0; round < 3; round++) await world.SyncAllAsync();

        foreach (var host in world.Hosts)
        {
            var property = (await host.PropertiesAsync()).Single();
            Assert.AreEqual((expected, PropertyType.MultipleChoice), (property.Name, property.Type), host.Name);
            CollectionAssert.AreEquivalent(new[] {"Rock", "Jazz", "Pop"},
                SyncOptions.Read(property).Select(o => o.Label).ToArray(), host.Name);
            Assert.AreEqual(0, (await host.OpenItemsAsync()).Count, await world.DigestAsync(true));
        }
    }

    /// <summary>
    /// Both devices change the type, then rename it apart: the name conflict froze an ordinary merge, not a Convert, so
    /// its decision keeps a setting only one device changed, on both.
    /// </summary>
    [TestMethod]
    public async Task Both_convert_then_a_name_conflict_keeps_a_setting_only_one_side_changed()
    {
        var world = new SyncWorld();
        var a = await world.AddHostAsync("A");
        var b = await world.AddHostAsync("B");
        await a.AddPropertyAsync("Genre", PropertyType.SingleChoice, "Rock", "Jazz");
        await world.LinkAsync(b, a, DataSyncLinkMode.TwoWay);
        foreach (var host in world.Hosts)
        {
            var own = await host.PropertyAsync("Genre");
            foreach (var option in SyncOptions.Read(own)) await host.UseAsync(own, option.Id);
            await host.ChangeTypeAsync(own, PropertyType.MultipleChoice);
        }

        var converted = await a.PropertyAsync("Genre");
        await a.PutAsync(converted, "Genre A", true, SyncOptions.Read(converted));
        await b.RenameAsync("Genre", "Genre B");
        await world.SyncAllAsync();
        var name = (await a.OpenItemsAsync()).Single(i => i.SubjectPath == "name");
        Assert.IsNull(await a.ResolveAsync(name, DataSyncInboxAction.KeepLocal));
        for (var round = 0; round < 3; round++) await world.SyncAllAsync();

        foreach (var host in world.Hosts)
        {
            var property = (await host.PropertiesAsync()).Single();
            Assert.AreEqual(("Genre A", true), (property.Name, SyncOptions.IgnoreCase(property)), host.Name);
            Assert.AreEqual(0, (await host.OpenItemsAsync()).Count, await world.DigestAsync(true));
        }
    }

    [TestMethod]
    public async Task A_copy_once_onto_a_stopped_link_takes_the_peers_values_and_removes_nothing()
    {
        var world = new SyncWorld();
        var a = await world.AddHostAsync("A");
        var b = await world.AddHostAsync("B");
        await a.AddPropertyAsync("Genre", PropertyType.MultipleChoice, "Rock", "Jazz", "Pop");
        await world.LinkAsync(b, a, DataSyncLinkMode.TwoWay);
        var link = await b.Node.RequireLinkToAsync(a.Node);
        Assert.IsNull((await b.CallAsync(s => s.UpdateLinkAsync(link.Id,
            new DataSyncLinkUpdateInput(DataSyncLinkMode.Off, null), true, default))).Problem);

        // A renames Genre and Rock, and removes Jazz; B's Genre was last edited by A.
        await a.EditAsync("Genre", o => o.Where(x => x.Label != "Jazz")
            .Select(x => x.Label == "Rock" ? x with {Label = "Rock!"} : x), "Genre 2");
        await CopyOnceAsync(world, b, a);

        var copied = (await b.PropertiesAsync()).Single();
        Assert.AreEqual("Genre 2", copied.Name);
        CollectionAssert.AreEquivalent(new[] {"Rock!", "Jazz", "Pop"},
            SyncOptions.Read(copied).Select(o => o.Label).ToArray());
        Assert.AreEqual(DataSyncLinkState.Stopped, (await b.Node.RequireLinkToAsync(a.Node)).State);
    }

    [TestMethod]
    public async Task A_copy_once_previews_a_fresh_devices_name_matches_and_applies_nothing_past_one_it_missed()
    {
        var world = new SyncWorld();
        var a = await world.AddHostAsync("A");
        var b = await world.AddHostAsync("B");
        await a.AddPropertyAsync("Genre", PropertyType.MultipleChoice, "Rock", "Jazz");
        await a.AddPropertyAsync("Mood", PropertyType.MultipleChoice, "Calm");
        var own = await b.AddPropertyAsync("Genre", PropertyType.MultipleChoice, "Rock", "Pop");
        await b.UseAsync(own, SyncOptions.Read(own)[0].Id);

        // B has no link and no reader: its first Refresh comes with the snapshot, so the preview knows B's Genre.
        await CopyOnceAsync(world, b, a, start: false);
        var link = await b.Node.RequireLinkToAsync(a.Node);
        var entries = (await b.CallAsync(s => s.GetFirstSyncAsync(link.Id, default))).Entries;
        var genre = entries.Single(e => e.Name == "Genre");
        Assert.AreEqual(DataSyncPreviewOutcome.NameMatch, genre.Outcome);
        Assert.AreEqual(own.Id.ToString(), genre.Candidates!.Single().LocalKey);
        Assert.AreEqual(DataSyncPreviewOutcome.Create, entries.Single(e => e.Name == "Mood").Outcome);

        // B makes its own Mood after the preview: the Start meets a name match nobody answered and applies nothing.
        await b.AddPropertyAsync("Mood", PropertyType.MultipleChoice, "Calm");
        DataSyncFirstSyncChoice[] choices =
            [new(DataSyncKindIds.CustomProperty, genre.Key, DataSyncFirstSyncAction.Link, own.Id.ToString())];
        await b.CallAsync(s => s.StartFirstSyncAsync(link.Id, new DataSyncFirstSyncStartInput(choices), default));
        await b.Node.RunWriteTasksAsync(id => id.StartsWith("DataSyncReview", StringComparison.Ordinal));
        Assert.AreEqual(2, (await b.PropertiesAsync()).Count);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, (await b.Node.RequireLinkToAsync(a.Node)).State);

        // The next preview shows it; answered, the copy goes through.
        await world.SyncAsync(b);
        var mood = (await b.CallAsync(s => s.GetFirstSyncAsync(link.Id, default))).Entries
            .Single(e => e.Name == "Mood");
        Assert.AreEqual(DataSyncPreviewOutcome.NameMatch, mood.Outcome);
        await b.Node.StartFirstSyncAsync(link, [.. choices, new(DataSyncKindIds.CustomProperty, mood.Key,
            DataSyncFirstSyncAction.Link, mood.Candidates!.Single().LocalKey)]);
        var properties = await b.PropertiesAsync();
        Assert.AreEqual(2, properties.Count);
        CollectionAssert.AreEquivalent(new[] {"Rock", "Pop", "Jazz"},
            SyncOptions.Read(properties.Single(p => p.Name == "Genre")).Select(o => o.Label).ToArray());
        Assert.AreEqual(DataSyncLinkState.Stopped, (await b.Node.RequireLinkToAsync(a.Node)).State);
    }

    /// <summary>A copy once from <paramref name="source"/>, approved there, its snapshot staged and started.</summary>
    private static async Task CopyOnceAsync(SyncWorld world, SyncHost reader, SyncHost source, bool start = true)
    {
        var created = await reader.CallAsync(s => s.CreateCopyOnceAsync(
            new DataSyncCopyOnceInput(source.NodeId, null, null, [.. DataSyncKindIds.All]), true, default));
        Assert.IsNull(created.Problem, created.Problem?.Code.ToString());
        if (created.RequestId is { } requestId)
        {
            Assert.IsNull((await source.CallAsync(s => s.ApproveRequestAsync(requestId,
                new DataSyncApproveInput(false, null), default))).Problem);
            world.Network.Claim(reader.NodeId);
        }

        await world.SyncAsync(reader);
        Assert.IsTrue(reader.Node.HasPreview(await reader.Node.RequireLinkToAsync(source.Node)));
        if (start) await reader.StartFirstSyncAsync(source);
    }
}
