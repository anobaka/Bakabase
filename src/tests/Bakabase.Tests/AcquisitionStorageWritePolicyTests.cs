using System.Data.Common;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
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
            var commands = new PolicyQueryRecorder();
            var dbOptions = new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=" + Path.Combine(root, "isolated.db"))
                .AddInterceptors(commands).Options;
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
            commands.Reads.Clear();
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
            // The emergency path must recheck live permission without loading the file manifest or plan for each write.
            await using (var db = new BakabaseDbContext(dbOptions))
            {
                db.AcquisitionTasks.Add(new() {Id = 7, WorkflowRunId = 17, PlanRevision = 1});
                await db.SaveChangesAsync();
            }
            options.Value.UseEmergencySpaceReserve = true;
            options.Value.MinimumFreeSpaceBytes = 100L * 1024 * 1024 * 1024;
            commands.Reads.Clear();
            for (var i = 0; i < 2; i++)
                Assert.AreEqual(AcquisitionStorageOverrideGrant.EmergencyMinimumFreeBytes,
                    await provider.GetMinimumFreeBytesAsync(new("workflow", "17")));
            var runReads = commands.Reads.Where(sql => sql.Contains("FROM \"WorkflowRuns\"")).ToList();
            Assert.AreEqual(2, runReads.Count, "Lowered permission must be re-evaluated for each write.");
            foreach (var sql in runReads)
            {
                StringAssert.Contains(sql, "\"Status\"");
                Assert.IsFalse(sql.Contains("CurrentItemJson") || sql.Contains("OutputItemsJson") || sql.Contains("PlanSnapshotJson"), sql);
            }
            var taskReads = commands.Reads.Where(sql => sql.Contains("FROM \"AcquisitionTasks\"")).ToList();
            Assert.AreEqual(2, taskReads.Count);
            foreach (var sql in taskReads)
            {
                StringAssert.Contains(sql, "BuilderStateJson");
                Assert.IsFalse(sql.Contains("PreparedContextJson") || sql.Contains("PlanHistoryJson") || sql.Contains("PlanJson"), sql);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class PolicyQueryRecorder : DbCommandInterceptor
    {
        public List<string> Reads { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Reads.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Options : IBOptions<AcquisitionOptions>
    {
        public AcquisitionOptions Value { get; } = new();
    }
}
