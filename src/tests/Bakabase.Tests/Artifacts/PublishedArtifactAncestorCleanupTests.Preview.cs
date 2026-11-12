using Microsoft.EntityFrameworkCore;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    public async Task A_historical_unavailable_delivery_requires_acceptance_instead_of_silently_omitting_ancestors()
    {
        var fixture = await DeletionFixture();
        await db.Artifacts.Where(a => a.Id == fixture.Final.Id).ExecuteUpdateAsync(update =>
            update.SetProperty(a => a.State, ArtifactLocationState.Missing));
        var unaccepted = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, false));
        Assert.IsFalse(unaccepted.Rows.Single().CanDelete);
        Assert.AreEqual("CLEANUP_DELIVERY_ACCEPTANCE_REQUIRED", unaccepted.Rows.Single().ReasonCode);
        Assert.AreEqual(ArtifactLocationState.Missing, (await store.GetArtifactAsync(fixture.Final.Id))!.State,
            "Preview must not rewrite an old observation as a side effect of inspecting metadata.");
        var accepted = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true));
        Assert.IsTrue(accepted.Rows.Single().CanDelete, accepted.Rows.Single().ReasonCode);
        Assert.IsTrue(accepted.Rows.Single().Cleanup!.Groups.Any(group => group.ArtifactId == fixture.Source.Id));
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
        Assert.IsTrue(File.Exists(fixture.Final.Files.Single()));
    }

    [TestMethod]
    public async Task Deletion_preview_does_not_read_delivery_bytes_or_mutate_observation_and_apply_still_verifies_them()
    {
        var fixture = await DeletionFixture();
        var before = await db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == fixture.Final.Id);
        string previewId;
        await using (var locked = new FileStream(fixture.Final.Files.Single(), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, false));
            Assert.IsTrue(preview.Rows.Single().CanDelete, preview.Rows.Single().ReasonCode);
            previewId = preview.PreviewId;
        }
        var after = await db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == fixture.Final.Id);
        Assert.AreEqual(before.UpdatedAt, after.UpdatedAt);
        Assert.AreEqual(before.State, after.State);
        var file = fixture.Final.Files.Single(); var timestamp = File.GetLastWriteTimeUtc(file);
        await File.WriteAllTextAsync(file, "fake bytes"); // Same length as "same bytes".
        File.SetLastWriteTimeUtc(file, timestamp);
        var result = await fixture.Service.DeleteAsync(new(previewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
        Assert.AreEqual("fake bytes", await File.ReadAllTextAsync(file));
    }
}
