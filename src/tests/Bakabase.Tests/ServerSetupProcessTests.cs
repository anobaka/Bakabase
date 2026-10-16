using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Service.Components.ServerData;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize]
public class ServerSetupProcessTests
{
    private string _root = null!;
    [TestInitialize] public void Initialize() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "bakabase-process-test-" + Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public async Task InternalRolesCannotLaunchWithoutTheirPrivateParentCapability()
    {
        var pipe = Environment.GetEnvironmentVariable(SetupChildConnection.PipeEnvironment);
        var secret = Environment.GetEnvironmentVariable(SetupChildConnection.SecretEnvironment);
        try
        {
            Environment.SetEnvironmentVariable(SetupChildConnection.PipeEnvironment, null);
            Environment.SetEnvironmentVariable(SetupChildConnection.SecretEnvironment, null);
            Assert.IsNull(await SetupChildConnection.ConnectAsync([]));
            await Assert.ThrowsExactlyAsync<IOException>(() => SetupChildConnection.ConnectAsync(["--bakabase-role=worker"]));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => SetupChildConnection.ConnectAsync(["--bakabase-role=unknown"]));
            Assert.AreEqual(0, Directory.GetFileSystemEntries(_root).Length);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SetupChildConnection.PipeEnvironment, pipe);
            Environment.SetEnvironmentVariable(SetupChildConnection.SecretEnvironment, secret);
        }
    }

    [TestMethod]
    public void LaunchContractIsShellFreeAndNeverRecursivelyCarriesAnOldRole()
    {
        var info = SetupChildConnection.ChildStartInfo("worker", ["--example=value", "--bakabase-role=business"], "private-pipe", new string('A', 64));
        Assert.IsFalse(info.UseShellExecute);
        CollectionAssert.Contains(info.ArgumentList.ToArray(), "--example=value");
        Assert.AreEqual(1, info.ArgumentList.Count(a => a.StartsWith(SetupChildConnection.RoleArgument)));
        Assert.AreEqual("--bakabase-role=worker", info.ArgumentList.Last());
        Assert.AreEqual("private-pipe", info.Environment[SetupChildConnection.PipeEnvironment]);
        Assert.IsFalse(info.ArgumentList.Any(a => a.Contains(new string('A', 64))), "IPC credentials must not appear on the command line.");
    }

    [TestMethod]
    public void Named_workspace_children_discard_the_coordinators_old_environment_override()
    {
        var previous = Environment.GetEnvironmentVariable("BAKABASE_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("BAKABASE_DATA_DIR", Path.Combine(_root, "old-environment"));
            var named = SetupChildConnection.ChildStartInfo("business", ["--profile=default"], "private-pipe", new string('A', 64));
            Assert.IsFalse(named.Environment.ContainsKey("BAKABASE_DATA_DIR"));
            var legacy = SetupChildConnection.ChildStartInfo("business", [], "private-pipe", new string('A', 64));
            Assert.AreEqual(Path.Combine(_root, "old-environment"), legacy.Environment["BAKABASE_DATA_DIR"]);
        }
        finally { Environment.SetEnvironmentVariable("BAKABASE_DATA_DIR", previous); }
    }

    [TestMethod]
    public async Task Reused_desktop_coordinator_initializes_Setup_only_at_the_explicit_new_workspace_anchor()
    {
        var previous = Environment.GetEnvironmentVariable("BAKABASE_DATA_DIR");
        var oldDirectory = Path.Combine(_root, "old-environment");
        var anchor = Path.Combine(_root, "next-anchor");
        var data = Path.Combine(_root, "next-data");
        Directory.CreateDirectory(anchor);
        Bakabase.Infrastructures.Components.App.AnchorRedirect.Write(anchor, data);
        using var stopping = new System.Threading.CancellationTokenSource();
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? running = null;
        try
        {
            Environment.SetEnvironmentVariable("BAKABASE_DATA_DIR", oldDirectory);
            running = SetupProcessCoordinator.RunAsync(new SetupCoordinatorOptions
            {
                IsDesktop = true, AnchorDirectory = anchor, EnvironmentOverride = false,
                RecoverStartupFailureExternally = true,
                OnSetupUrl = _ => { opened.TrySetResult(); return Task.CompletedTask; }
            }, stopping.Token);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var status = ServerSetupSession.Current!.Read();
            Assert.AreEqual(ServerSetupSession.Canonical(data), status.CurrentPath);
            Assert.IsTrue(status.CanChooseTargetPath, "The original coordinator ENV must not fix another workspace's directory.");
            Assert.IsFalse(Directory.Exists(oldDirectory));
            Assert.IsTrue(File.Exists(Path.Combine(anchor, ServerSetupSession.FileName)));
        }
        finally
        {
            stopping.Cancel();
            if (running != null) await running.WaitAsync(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("BAKABASE_DATA_DIR", previous);
        }
        using var unlocked = DataDirectoryLock.TryAcquire(data).Lock;
        Assert.IsNotNull(unlocked);
    }

    [TestMethod]
    public void PassiveParentCanFollowWorkerProgressWithoutBecomingItsWriter()
    {
        using var writer = new ImportProgressStore(_root);
        writer.Begin(new ServerAppDataImport.Journal());
        using var parent = new ImportProgressStore(_root, readOnly: true);
        var initial = parent.Read();
        var token = parent.Token;
        writer.Report(new AppDataImportProgressUpdate("copying", 25, 100, 0, 1, "library.db"));
        writer.Fail("Worker interrupted.");
        parent.RefreshFromDisk(_root);
        Assert.IsTrue(parent.TryReadAuthorized(token, out var failure));
        Assert.AreEqual("failed", failure!.Phase);
        Assert.AreEqual("copying", failure.FailedPhase);
        Assert.AreEqual("library.db", failure.CurrentFile);
        Assert.AreEqual("scanning", initial!.Phase, "Published snapshots must remain immutable.");
    }

    [TestMethod]
    public void FailedPersistenceDoesNotLetDiskRefreshHideTheWorkerFailure()
    {
        using (var writer = new ImportProgressStore(_root)) writer.Begin(new ServerAppDataImport.Journal());
        var path = Path.Combine(_root, ImportProgressStore.FileName);
        var original = File.ReadAllBytes(path);
        using var parent = new ImportProgressStore(_root, readOnly: true);
        parent.FailReadOnly("Worker exited and the data disk cannot be written.");
        parent.RefreshFromDisk(_root);
        Assert.AreEqual("failed", parent.Read()!.Phase);
        StringAssert.Contains(parent.Read()!.Error!, "Worker exited");
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path), "A read-only failure must not modify an unowned directory.");
    }

    [TestMethod]
    public void ParentSetupCanPersistAPlanThroughItsPassiveMonitoringStore()
    {
        using var parent = new ImportProgressStore(_root, readOnly: true);
        var journal = new ServerAppDataImport.Journal();
        parent.EnsureQueued(journal);
        var token = parent.Token;
        using var worker = new ImportProgressStore(_root);
        Assert.IsTrue(worker.Authorize(token));
        Assert.AreEqual(journal.Id, worker.Read()!.Id);
        Assert.AreEqual("queued", worker.Read()!.Phase);
    }
    [TestMethod]
    public void RelayAcknowledgementMustMatchTheCommittedSetupCapability()
    {
        using var held = DataDirectoryLock.TryAcquire(_root).Lock!;
        using var setup = new ServerSetupSession(_root, _root, true, held);
        var token = setup.Token;
        setup.Commit(new ServerSetupSession.SetupRequest { Operation = "initialize" }, token);
        setup.SignalCommitted(new string('z', 64));
        Assert.IsFalse(setup.Completion.IsCompleted, "An unrelated relay must not release the setup data lock.");
        setup.SignalCommitted(token);
        Assert.IsTrue(setup.Completion.IsCompletedSuccessfully);
        setup.Monitoring?.Dispose();
    }

}
