using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.SingleInstance;

/// <summary>
/// The guard is process-wide state, as it has to be; every test starts and ends with it
/// released.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SingleInstanceGuardTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        SingleInstanceGuard.ResetForTests();
        _root = Path.Combine(Path.GetTempPath(), "bakabase-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SingleInstanceGuard.ResetForTests();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string Dir(string name) => Path.Combine(_root, name);

    /// <summary>BAKABASE_DATA_DIR for a probe's <c>launch</c>.</summary>
    private static Dictionary<string, string?> DataDirectoryVariable(string anchor) =>
        new() {[DefaultAppDataPathResolver.EnvVarName] = anchor};

    private static TaskCompletionSource ActivationProbe()
    {
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SingleInstanceGuard.SetActivationHandler(() => activated.TrySetResult());
        return activated;
    }

    [TestMethod]
    public void Entering_twice_is_a_no_op()
    {
        var dir = Dir("data");
        Assert.AreEqual(SingleInstanceEntry.Entered, SingleInstanceGuard.Enter(dir));
        Assert.AreEqual(SingleInstanceEntry.Entered, SingleInstanceGuard.Enter(dir),
            "the entry point and AppHost both call it; the second must not trip over the first");
        Assert.AreEqual(dir, SingleInstanceGuard.PrimaryDirectory);
        StringAssert.Contains(SingleInstanceGuard.Describe(), dir);
    }

    [TestMethod]
    public async Task A_second_launch_is_refused_and_brings_the_owner_forward()
    {
        var dir = Dir("data");
        Assert.AreEqual(SingleInstanceEntry.Entered, SingleInstanceGuard.Enter(dir));
        var activated = ActivationProbe();

        using var second = await InstanceProbe.StartAsync("enter", dir);
        Assert.AreEqual("ENTRY Refused", second.FirstLine);
        await activated.Task.WaitAsync(Wait);
    }

    [TestMethod]
    public async Task A_refused_launch_here_reaches_the_owner_in_another_process()
    {
        var dir = Dir("data");
        using var owner = await InstanceProbe.StartAsync("enter", dir);
        Assert.AreEqual("ENTRY Entered", owner.FirstLine);

        Assert.AreEqual(SingleInstanceEntry.Refused, SingleInstanceGuard.Enter(dir));
        Assert.AreEqual("ACTIVATED", await owner.ReadLineAsync(Wait));
        Assert.IsNull(SingleInstanceGuard.PrimaryDirectory, "a refused launch owns nothing");
    }

    [TestMethod]
    public async Task A_request_that_arrives_before_the_window_exists_is_delivered_later()
    {
        var dir = Dir("data");
        SingleInstanceGuard.Enter(dir);

        using (var second = await InstanceProbe.StartAsync("enter", dir))
        {
            Assert.AreEqual("ENTRY Refused", second.FirstLine);
        }

        // Nothing was listening for it yet (a relocation still copying, say). It is not lost.
        await Task.Delay(300);
        var activated = ActivationProbe();
        await activated.Task.WaitAsync(Wait);
    }

    [TestMethod]
    public async Task A_data_directory_variable_whose_redirect_names_an_owned_directory_is_refused()
    {
        // This process owns the data directory; a second launch names the anchor that redirects
        // to it through BAKABASE_DATA_DIR. The database and app.json follow the redirect, so the
        // guard has to as well — it used to stop at the anchor, lock that, and let the second
        // launch open this process's database.
        var target = Dir("data");
        var anchor = Dir("anchor");
        AnchorRedirect.Write(anchor, target);
        Assert.AreEqual(SingleInstanceEntry.Entered, SingleInstanceGuard.Enter(target));
        var activated = ActivationProbe();

        using var launch = await InstanceProbe.StartAsync("launch", null, DataDirectoryVariable(anchor));
        Assert.AreEqual("ENTRY Refused", launch.FirstLine);
        await activated.Task.WaitAsync(Wait);

        CollectionAssert.AreEqual(new[] {AnchorRedirect.FileName},
            Directory.EnumerateFileSystemEntries(anchor).Select(Path.GetFileName).ToArray(),
            "a refused launch leaves nothing behind, not even a lock file on the anchor");
    }

    [TestMethod]
    public async Task Through_a_data_directory_variable_the_guard_owns_where_app_json_is_read()
    {
        var target = Dir("data");
        var anchor = Dir("anchor");
        AnchorRedirect.Write(anchor, target);

        using var launch = await InstanceProbe.StartAsync("launch", null, DataDirectoryVariable(anchor));
        Assert.AreEqual("ENTRY Entered", launch.FirstLine);
        Assert.AreEqual($"APPJSON {Path.Combine(target, "app.json")}", await launch.ReadLineAsync(Wait),
            "app.json, and the database beside it, are in the directory the guard owns");

        // The same instance, whichever way it is reached.
        Assert.AreEqual(SingleInstanceEntry.Refused, SingleInstanceGuard.Enter(target));
        Assert.AreEqual("ACTIVATED", await launch.ReadLineAsync(Wait));
    }

    [TestMethod]
    public async Task A_launch_with_another_temp_directory_still_reaches_the_owner()
    {
        // .NET puts a plain-named pipe in the caller's own $TMPDIR, which an ssh session, a
        // script or an IDE may have set differently from the running app's: refused by the lock,
        // the launch then knocked on a socket that was not there. Windows pipes have no directory.
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Windows pipe names do not depend on TMPDIR.");

        var dir = Dir("data");
        var ownerTemp = Directory.CreateDirectory(Dir("tmp-owner")).FullName + "/";
        var launchTemp = Directory.CreateDirectory(Dir("tmp-launch")).FullName + "/";

        using var owner = await InstanceProbe.StartAsync("enter", dir,
            new Dictionary<string, string?> {["TMPDIR"] = ownerTemp});
        Assert.AreEqual("ENTRY Entered", owner.FirstLine);

        using (var second = await InstanceProbe.StartAsync("enter", dir,
                   new Dictionary<string, string?> {["TMPDIR"] = launchTemp}))
        {
            Assert.AreEqual("ENTRY Refused", second.FirstLine);
        }

        Assert.AreEqual("ACTIVATED", await owner.ReadLineAsync(Wait));

        // And from this process, whose TMPDIR is neither of theirs.
        Assert.AreEqual(SingleInstanceEntry.Refused, SingleInstanceGuard.Enter(dir));
        Assert.AreEqual("ACTIVATED", await owner.ReadLineAsync(Wait));

        foreach (var temp in new[] {ownerTemp, launchTemp})
        {
            Assert.IsFalse(Directory.EnumerateFileSystemEntries(temp)
                    .Select(Path.GetFileName)
                    .Any(name => name!.StartsWith("CoreFxPipe_", StringComparison.Ordinal) ||
                                 name.StartsWith("Bakabase-", StringComparison.Ordinal)),
                $"the channel's socket is not in {temp}");
        }
    }

    [TestMethod]
    public async Task Different_data_directories_do_not_conflict()
    {
        SingleInstanceGuard.Enter(Dir("one"));

        using var other = await InstanceProbe.StartAsync("enter", Dir("two"));
        Assert.AreEqual("ENTRY Entered", other.FirstLine, "BAKABASE_DATA_DIR elsewhere is its own instance");
    }

    [TestMethod]
    public async Task When_the_effective_directory_moves_the_old_one_is_let_go()
    {
        // What a legacy layout converted to a redirect looks like to the entry point.
        var before = Dir("anchor");
        var after = Dir("target");
        SingleInstanceGuard.Enter(before);
        Assert.AreEqual(SingleInstanceEntry.Entered, SingleInstanceGuard.Enter(after));
        Assert.AreEqual(after, SingleInstanceGuard.PrimaryDirectory);

        using var onBefore = await InstanceProbe.StartAsync("hold", before);
        Assert.AreEqual("LOCK Acquired", onBefore.FirstLine);
        using var onAfter = await InstanceProbe.StartAsync("hold", after);
        Assert.AreEqual("LOCK HeldByAnotherProcess", onAfter.FirstLine);
    }

    [TestMethod]
    public async Task During_a_relocation_both_directories_are_owned_and_answer()
    {
        var source = Dir("source");
        var target = Dir("target");
        SingleInstanceGuard.Enter(source);
        var activated = ActivationProbe();

        Assert.AreEqual(SingleInstanceEntry.Entered, SingleInstanceGuard.AcquireAdditional(target));

        // Mid-move: a launch pointed at either directory is refused, and reaches this process.
        using (var viaTarget = await InstanceProbe.StartAsync("enter", target))
        {
            Assert.AreEqual("ENTRY Refused", viaTarget.FirstLine);
        }

        await activated.Task.WaitAsync(Wait);

        using (var viaSource = await InstanceProbe.StartAsync("enter", source))
        {
            Assert.AreEqual("ENTRY Refused", viaSource.FirstLine);
        }

        // The move went through: the runner emptied the source but for its lock file.
        SingleInstanceGuard.Promote(target);
        Assert.AreEqual(target, SingleInstanceGuard.PrimaryDirectory);
        SingleInstanceGuard.Retire(source);

        Assert.IsFalse(Directory.Exists(source), "an emptied source goes, lock file and all");
        using var onTarget = await InstanceProbe.StartAsync("hold", target);
        Assert.AreEqual("LOCK HeldByAnotherProcess", onTarget.FirstLine);
    }

    [TestMethod]
    public void Retiring_a_source_that_still_has_data_keeps_the_data()
    {
        // UseTarget mode, or a source that is the anchor: the files stay, only the lock goes.
        var source = Dir("source");
        var target = Dir("target");
        SingleInstanceGuard.Enter(source);
        File.WriteAllText(Path.Combine(source, "bakabase_insideworld.db"), "x");
        SingleInstanceGuard.AcquireAdditional(target);
        SingleInstanceGuard.Promote(target);

        SingleInstanceGuard.Retire(source);

        Assert.IsTrue(File.Exists(Path.Combine(source, "bakabase_insideworld.db")));
        Assert.IsFalse(File.Exists(DataDirectoryLock.GetLockFilePath(source)));
    }

    [TestMethod]
    public void The_primary_is_never_retired()
    {
        var dir = Dir("data");
        SingleInstanceGuard.Enter(dir);
        SingleInstanceGuard.Retire(dir);
        SingleInstanceGuard.Abandon(dir);

        Assert.AreEqual(dir, SingleInstanceGuard.PrimaryDirectory);
        Assert.AreEqual(DataDirectoryLockStatus.HeldByAnotherProcess, DataDirectoryLock.TryAcquire(dir).Status);
    }

    [TestMethod]
    public void An_abandoned_target_is_put_back_as_it_was()
    {
        SingleInstanceGuard.Enter(Dir("source"));

        var created = Dir("new-target");
        SingleInstanceGuard.AcquireAdditional(created);
        Assert.IsTrue(Directory.Exists(created));
        SingleInstanceGuard.Abandon(created);
        Assert.IsFalse(Directory.Exists(created), "taking the lock created it, so giving up removes it");

        var existing = Dir("existing-target");
        Directory.CreateDirectory(existing);
        SingleInstanceGuard.AcquireAdditional(existing);
        SingleInstanceGuard.Abandon(existing);
        Assert.IsTrue(Directory.Exists(existing), "a directory the user made stays");
        Assert.IsFalse(File.Exists(DataDirectoryLock.GetLockFilePath(existing)));
    }

    [TestMethod]
    public async Task A_relocation_target_another_instance_owns_is_refused_without_handing_off()
    {
        SingleInstanceGuard.Enter(Dir("source"));
        var target = Dir("target");
        using var other = await InstanceProbe.StartAsync("enter", target);
        Assert.AreEqual("ENTRY Entered", other.FirstLine);

        Assert.AreEqual(SingleInstanceEntry.Refused, SingleInstanceGuard.AcquireAdditional(target));
        Assert.IsNull(await other.ReadLineAsync(TimeSpan.FromSeconds(1)),
            "the move waits; it has no business raising the other instance's window");
        Assert.AreEqual(Dir("source"), SingleInstanceGuard.PrimaryDirectory, "and this process keeps running");
    }

    [TestMethod]
    public void A_build_without_the_guard_touches_nothing()
    {
        var dir = Dir("dev");
        Assert.IsTrue(SingleInstanceGuard.EnterOrHandOff(enabled: false, () => dir));
        Assert.IsFalse(Directory.Exists(dir));
        Assert.IsNull(SingleInstanceGuard.PrimaryDirectory);
    }

    [TestMethod]
    public void A_directory_that_cannot_be_locked_is_run_unguarded_rather_than_refused()
    {
        // Never lock the user out: a read-only volume, a missing permission or a file where the
        // directory should be says nothing about other instances.
        var blocker = Dir("file");
        File.WriteAllText(blocker, "x");

        Assert.AreEqual(SingleInstanceEntry.Unguarded, SingleInstanceGuard.Enter(Path.Combine(blocker, "data")));
        Assert.IsTrue(SingleInstanceGuard.EnterOrHandOff(enabled: true, () => Path.Combine(blocker, "data")));
        StringAssert.Contains(SingleInstanceGuard.Describe(), "not engaged");
    }

    [TestMethod]
    public void A_data_directory_that_cannot_be_resolved_is_left_to_the_rest_of_startup()
    {
        // A corrupt redirect throws by design; the startup that follows reports it into a log
        // file. The guard must not throw first, before any log exists.
        Assert.IsTrue(SingleInstanceGuard.EnterOrHandOff(enabled: true,
            () => throw new InvalidOperationException("Anchor redirect must contain an absolute path")));
        Assert.IsNull(SingleInstanceGuard.PrimaryDirectory);
    }

    [TestMethod]
    public async Task A_holder_that_never_answers_still_refuses()
    {
        // Something holds the lock but serves no channel: an instance whose channel is down,
        // say. The launch waits out the handoff, looks again, and is still refused.
        var dir = Dir("data");
        using var holder = await InstanceProbe.StartAsync("hold", dir);
        Assert.AreEqual("LOCK Acquired", holder.FirstLine);

        Assert.AreEqual(SingleInstanceEntry.Refused, SingleInstanceGuard.Enter(dir));
    }

    [TestMethod]
    public async Task A_holder_that_lets_go_while_the_launch_waits_is_taken_over()
    {
        // The holder was on its way out (or, on Windows, was a scanner with the file open):
        // after the unanswered handoff the launch looks again and starts.
        var dir = Dir("data");
        var holder = await InstanceProbe.StartAsync("hold", dir);
        Assert.AreEqual("LOCK Acquired", holder.FirstLine);
        var release = Task.Run(async () =>
        {
            await Task.Delay(1000);
            holder.Stop();
            holder.Dispose();
        });

        Assert.AreEqual(SingleInstanceEntry.Entered, SingleInstanceGuard.Enter(dir));
        await release;
    }
}
