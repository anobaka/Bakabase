using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionStorageWritePolicyTests
{
    [TestMethod]
    public async Task ExistingWorkflowCloudDownloadAndTransportLeaseUseLiveCallerReserve()
    {
        var root = Path.Combine(Path.GetTempPath(), "storage-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var dbOptions = new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=" + Path.Combine(root, "isolated.db")).Options;
            using var services = new ServiceCollection().AddScoped(_ => new BakabaseDbContext(dbOptions)).BuildServiceProvider();
            await using (var db = new BakabaseDbContext(dbOptions))
            {
                await db.Database.EnsureCreatedAsync();
                db.Set<WorkflowRunDbModel>().Add(new() {Id = 17, Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = "7"}]});
                db.Set<WorkflowRunDbModel>().Add(new() {Id = 18, Sources = [new() {Module = "other", EntityKind = "task", EntityId = "8"}]});
                db.CloudDownloadOperations.Add(new CloudOperationDbModel {Id = "cloud-old", WorkflowRunId = 17, DownloadTaskId = 100});
                db.Set<ArtifactOperationDbModel>().Add(new() {Id = "native-old", IntentKey = "native-old", LeaseOwner = "workflow:17:source"});
                db.Set<DownloadResultOwnerDbModel>().Add(new() {DownloadTaskId = 101, AcquisitionTaskId = 7, WorkflowRunId = 17});
                await db.SaveChangesAsync();
            }
            var options = new Options();
            var provider = new AcquisitionStorageWritePolicyProvider(services.GetRequiredService<IServiceScopeFactory>(), options);
            StorageWriteContext[] contexts = [new("workflow", "17"), new("downloadTask", "100"), new("downloadTask", "101"),
                new("cloudOperation", "cloud-old"), new("artifactOperation", "native-old")];
            foreach (var context in contexts) Assert.AreEqual(100L * 1024 * 1024 * 1024, await provider.GetMinimumFreeBytesAsync(context));
            Assert.AreEqual(0, await provider.GetMinimumFreeBytesAsync(new("workflow", "18")));
            Assert.AreEqual(0, await provider.GetMinimumFreeBytesAsync(new("downloadTask", "999")));
            options.Value.MinimumFreeSpaceBytes = 200;
            foreach (var context in contexts) Assert.AreEqual(200, await provider.GetMinimumFreeBytesAsync(context));
            Assert.AreEqual(300, await provider.GetMinimumFreeBytesAsync(new("workflow", "17", 300)));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Options : IBOptions<AcquisitionOptions>
    {
        public AcquisitionOptions Value { get; } = new();
    }
}
