using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Services;

namespace Bakabase.Tests.DataSync.Convergence;

[TestClass]
public class OneTimeSynchronizationTests
{
    [TestMethod]
    public async Task Receiving_then_stopping_keeps_definitions_and_can_resume_later()
    {
        var world = new SyncWorld();
        var source = await world.AddHostAsync("Source");
        var reader = await world.AddHostAsync("Reader");
        await source.AddPropertyAsync("Genre", PropertyType.MultipleChoice, "Rock", "Jazz");
        await world.LinkAsync(reader, source, DataSyncLinkMode.Follow);

        var received = (await reader.PropertiesAsync()).Single();
        var link = await reader.Node.RequireLinkToAsync(source.Node);
        Assert.IsNull((await reader.CallAsync(service => service.UpdateLinkAsync(link.Id,
            new DataSyncLinkUpdateInput(DataSyncLinkMode.Off, null), true, default))).Problem);
        Assert.AreEqual(DataSyncLinkState.Stopped, (await reader.Node.RequireLinkToAsync(source.Node)).State);

        await source.RenameAsync("Genre", "Updated genre");
        await world.SyncAllAsync();
        var kept = (await reader.PropertiesAsync()).Single();
        Assert.AreEqual(received.Id, kept.Id);
        Assert.AreEqual("Genre", kept.Name);
        CollectionAssert.AreEquivalent(new[] { "Rock", "Jazz" },
            SyncOptions.Read(kept).Select(option => option.Label).ToArray());

        Assert.IsNull((await reader.CallAsync(service => service.UpdateLinkAsync(link.Id,
            new DataSyncLinkUpdateInput(DataSyncLinkMode.Follow, null), true, default))).Problem);
        await world.SyncAsync(reader);
        var resumed = (await reader.PropertiesAsync()).Single();
        Assert.AreEqual(received.Id, resumed.Id);
        Assert.AreEqual("Updated genre", resumed.Name);
    }
}
