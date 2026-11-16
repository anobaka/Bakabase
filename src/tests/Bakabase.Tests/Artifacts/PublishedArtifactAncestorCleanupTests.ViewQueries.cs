using System.Data.Common;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    public async Task Cleanup_view_closures_batch_descendant_ownership_and_recheck_later_references()
    {
        var f = await DeletionFixture();
        var consent = new ArtifactDeliveryCleanupConsent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            f.Task.Id, f.Task.PlanRevision, 1, true, [1],
            [new(f.Final.Id, f.Final.OperationId, f.Final.Fingerprint, f.Final.RootDirectory)], true, true);
        var descendants = new List<ArtifactRecord>();
        for (var i = 0; i < 32; i++) descendants.Add(await Artifact("processedFiles", f.Final));
        var observer = new CleanupViewQueryObserver();
        await using var context = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(root, "fixture.db")).AddInterceptors(observer).Options);
        var observedStore = new ArtifactOperationStore(context, () => root);
        CollectionAssert.AreEqual(new[] {f.Final.Id}, (await AcquisitionTaskCleanupViews.ReadAsync(context, observedStore, consent, default)).ToArray());
        Assert.IsTrue(observer.Reads <= 10, $"A 32-child closure used {observer.Reads} reads instead of batched ownership queries.");

        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "outside-owner", descendants[15].OperationId));
        Assert.AreEqual(0, (await AcquisitionTaskCleanupViews.ReadAsync(context, observedStore, consent, default)).Count,
            "The prior check cannot cache away an outside owner added after preview.");
        await db.ArtifactOperationBindings.Where(b => b.OperationId == descendants[15].OperationId && b.OwnerId == "2").ExecuteDeleteAsync();
        Assert.IsTrue(await store.AttachConsumerAsync(descendants[31].OperationId, "live-reader", TimeSpan.FromHours(1)));
        Assert.AreEqual(0, (await AcquisitionTaskCleanupViews.ReadAsync(context, observedStore, consent, default)).Count);
        await store.ReleaseConsumerAsync(descendants[31].OperationId, "live-reader");
        await Artifact("processedFiles", descendants[0], owner: 3);
        Assert.AreEqual(0, (await AcquisitionTaskCleanupViews.ReadAsync(context, observedStore, consent, default)).Count,
            "An outside grandchild still protects the whole input closure.");
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    private sealed class CleanupViewQueryObserver : DbCommandInterceptor
    {
        internal int Reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(result);
        }
    }
}
