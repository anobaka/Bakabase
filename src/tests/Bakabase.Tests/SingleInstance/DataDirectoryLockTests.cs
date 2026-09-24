using System;
using System.IO;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.SingleInstance;

[TestClass]
public class DataDirectoryLockTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [TestMethod]
    public async Task A_second_process_is_refused_while_the_first_holds_the_directory()
    {
        var dir = Path.Combine(_root, "data");

        using var first = await InstanceProbe.StartAsync("hold", dir);
        Assert.AreEqual("LOCK Acquired", first.FirstLine);

        using var second = await InstanceProbe.StartAsync("hold", dir);
        Assert.AreEqual("LOCK HeldByAnotherProcess", second.FirstLine,
            "the lock is the whole point: a second process on the same directory must not get it");

        var here = DataDirectoryLock.TryAcquire(dir);
        Assert.AreEqual(DataDirectoryLockStatus.HeldByAnotherProcess, here.Status);
        Assert.IsNull(here.Lock);
    }

    [TestMethod]
    public async Task A_hard_killed_holder_leaves_no_stale_lock()
    {
        var dir = Path.Combine(_root, "data");

        using var holder = await InstanceProbe.StartAsync("hold", dir);
        Assert.AreEqual("LOCK Acquired", holder.FirstLine);
        Assert.AreEqual(DataDirectoryLockStatus.HeldByAnotherProcess, DataDirectoryLock.TryAcquire(dir).Status);

        // SIGKILL on Unix: the process runs no cleanup at all, and the lock file stays on disk.
        holder.KillHard();
        Assert.IsTrue(File.Exists(DataDirectoryLock.GetLockFilePath(dir)), "the file itself is left behind");

        using var after = DataDirectoryLock.TryAcquire(dir).Lock;
        Assert.IsNotNull(after, "the operating system released the lock with the process; nothing is stale");
    }

    [TestMethod]
    public async Task A_lock_held_here_refuses_a_child_and_is_released_by_dispose()
    {
        var dir = Path.Combine(_root, "data");
        var held = DataDirectoryLock.TryAcquire(dir);
        Assert.IsTrue(held.Acquired);

        using (var child = await InstanceProbe.StartAsync("hold", dir))
        {
            Assert.AreEqual("LOCK HeldByAnotherProcess", child.FirstLine);
        }

        held.Lock!.Dispose();

        using var child2 = await InstanceProbe.StartAsync("hold", dir);
        Assert.AreEqual("LOCK Acquired", child2.FirstLine);
    }

    [TestMethod]
    public void Creates_a_missing_directory_and_says_so()
    {
        var dir = Path.Combine(_root, "not", "yet");

        using var held = DataDirectoryLock.TryAcquire(dir).Lock;
        Assert.IsNotNull(held);
        Assert.IsTrue(held.CreatedDirectory);
        Assert.IsTrue(File.Exists(DataDirectoryLock.GetLockFilePath(dir)));

        var existing = Path.Combine(_root, "existing");
        Directory.CreateDirectory(existing);
        using var second = DataDirectoryLock.TryAcquire(existing).Lock;
        Assert.IsFalse(second!.CreatedDirectory);
    }

    [TestMethod]
    public void A_path_that_cannot_be_a_directory_is_unavailable_not_held()
    {
        // A file where the directory should be: nothing to do with another instance, and must
        // not be reported as one (that would send a launch off to hand over to nobody).
        var blocker = Path.Combine(_root, "file");
        File.WriteAllText(blocker, "x");

        var attempt = DataDirectoryLock.TryAcquire(Path.Combine(blocker, "data"));
        Assert.AreEqual(DataDirectoryLockStatus.Unavailable, attempt.Status);
        Assert.IsNotNull(attempt.Error);
    }

    [TestMethod]
    public void Deleting_the_lock_file_refuses_while_held_and_succeeds_after()
    {
        var dir = Path.Combine(_root, "data");
        var held = DataDirectoryLock.TryAcquire(dir).Lock!;

        Assert.IsFalse(DataDirectoryLock.TryDeleteUnowned(dir), "a held lock file is never deleted");
        Assert.IsTrue(File.Exists(DataDirectoryLock.GetLockFilePath(dir)));

        held.Dispose();
        Assert.IsTrue(DataDirectoryLock.TryDeleteUnowned(dir));
        Assert.IsFalse(File.Exists(DataDirectoryLock.GetLockFilePath(dir)));
        Assert.IsTrue(DataDirectoryLock.TryDeleteUnowned(dir), "nothing to delete is success");
    }
}
