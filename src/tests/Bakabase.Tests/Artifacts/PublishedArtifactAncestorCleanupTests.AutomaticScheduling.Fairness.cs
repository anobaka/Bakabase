using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    public async Task Automatic_registered_pass_reaches_later_candidate_after_two_apply_time_blocks()
    {
        var oldest = await Artifact("processedFiles");
        var middle = await Artifact("processedFiles", oldest);
        var newest = await Artifact("processedFiles", middle);
        var published = await Artifact("publishedFiles", newest);
        var storage = new ApplyTimeStorage([newest.RootDirectory, middle.RootDirectory]);
        PublishedArtifactAncestorCleanup Worker() =>
            new(db, store, new ArchiveQuarantineService(store, Path.Combine(root, "appdata"), storage));
        cleanup = Worker();
        CollectionAssert.AreEqual(new[] {newest.Id, middle.Id, oldest.Id},
            (await Preview(published)).Where(row => row.Eligible).Select(row => row.ArtifactId).ToArray());
        await new PublishedArtifactCleanupCompletionObserver(db).StageAsync(
            new() {Id = 1, Status = WorkflowRunStatus.Success}, new object(), default);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        await cleanup.ReclaimRequestedAsync(published.Id, default);
        Assert.IsTrue(oldest.Files.All(File.Exists), "Keep the two actual apply attempts per pass.");
        CollectionAssert.AreEqual(new[] {published.Id}, (await cleanup.GetRequestedIdsAsync(default)).ToArray());
        CollectionAssert.AreEquivalent(new[] {newest.RootDirectory, middle.RootDirectory}, storage.Denied.ToArray());

        cleanup = Worker(); // A new worker still knows which registered candidates were attempted.
        await cleanup.ReclaimRequestedAsync(published.Id, default);
        Assert.AreEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(oldest.Id))!.State);
        Assert.IsFalse(oldest.Files.Any(File.Exists));
        Assert.IsTrue(middle.Files.All(File.Exists) && newest.Files.All(File.Exists) && published.Files.All(File.Exists));

        await cleanup.ReclaimRequestedAsync(published.Id, default);
        Assert.AreEqual(0, (await cleanup.GetRequestedIdsAsync(default)).Count);
        var status = JsonNode.Parse((await store.GetArtifactAsync(published.Id))!.MetadataJson!)!["publicationCleanup"]!;
        Assert.AreEqual("retained", status["status"]!.GetValue<string>());
        Assert.AreEqual(1, status["removedFiles"]!.GetValue<int>());
        CollectionAssert.Contains(status["reasons"]!.AsArray().Select(r => r!.GetValue<string>()).ToArray(),
            ApplyTimeStorage.DeniedReason);
    }

    private sealed class ApplyTimeStorage(IReadOnlyList<string> protectedRoots) : IUserStoragePolicy
    {
        internal const string DeniedReason = "Storage policy protects this registered input.";
        internal HashSet<string> Denied { get; } = new(StringComparer.Ordinal);
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            !protectedRoots.Contains(path, StringComparer.Ordinal);
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            if (IsPathAllowed(path, purpose)) return;
            Denied.Add(path);
            throw new UnauthorizedAccessException(DeniedReason);
        }
        public void EnsureTreeMutationAllowed(string path) => EnsurePathAllowed(path);
    }
}
