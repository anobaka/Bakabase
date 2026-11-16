namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Already_removed_owned_capture_additions_do_not_require_replacement_coverage(bool skip)
    {
        var fixture = await CapturedAddition(published: false, skip: skip);
        File.Delete(fixture.AddedFile);
        var candidate = (await CapturedPreview(fixture)).Single();
        Assert.IsTrue(candidate.Group.Eligible, candidate.Group.ReasonCode);
        Assert.AreEqual("AlreadyAbsent", candidate.Group.ReasonCode);
        Assert.AreEqual(0, candidate.Files.Count);
        var outcome = await fixture.Cleanup.DeleteCapturedInputAsync(candidate, fixture.Authorization, default);
        Assert.IsTrue(outcome.Completed, outcome.Reason);
        Assert.AreEqual(0, outcome.RemovedFiles);
        Assert.IsTrue(fixture.Source.Files.All(File.Exists));
        Assert.IsTrue(fixture.Publication.Files.All(File.Exists));
        Assert.IsTrue((await CapturedPreview(fixture)).Single().Group.Eligible);
    }

    [TestMethod]
    public async Task A_removed_capture_addition_reappearing_before_cleanup_still_requires_coverage()
    {
        var fixture = await CapturedAddition(published: false);
        File.Delete(fixture.AddedFile);
        var candidate = (await CapturedPreview(fixture)).Single();
        Assert.IsTrue(candidate.Group.Eligible);
        await File.WriteAllTextAsync(fixture.AddedFile, "new manual output");
        var outcome = await fixture.Cleanup.DeleteCapturedInputAsync(candidate, fixture.Authorization, default);
        Assert.IsFalse(outcome.Completed);
        Assert.AreEqual(0, outcome.RemovedFiles);
        Assert.IsTrue(File.Exists(fixture.AddedFile));
    }
}
