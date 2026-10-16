using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Profiles;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Tests.SingleInstance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Profiles;

[TestClass]
public class DesktopProfileProcessTests
{
    private string _root = null!;
    [TestInitialize] public void Initialize() => _root = Path.Combine(Path.GetTempPath(), "bakabase-profile-process-" + Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public async Task Independent_data_directory_guards_still_support_separate_headless_hosts()
    {
        var control = Path.Combine(_root, "control");
        var registry = new DesktopProfileRegistry(control, Path.Combine(control, "legacy"));
        var first = registry.Add("A", Path.Combine(_root, "A"), true);
        var second = registry.Add("B", Path.Combine(_root, "B"), true);
        using var a = await InstanceProbe.StartAsync("enter", Path.Combine(_root, "A"));
        Assert.AreEqual("ENTRY Entered", a.FirstLine);
        using var b = await InstanceProbe.StartAsync("enter", Path.Combine(_root, "B"));
        Assert.AreEqual("ENTRY Entered", b.FirstLine);
        using var duplicate = await InstanceProbe.StartAsync("enter", Path.Combine(_root, "A"));
        Assert.AreEqual("ENTRY Refused", duplicate.FirstLine);
        Assert.AreEqual("ACTIVATED", await a.ReadLineAsync(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(b.HasExited);
    }

    [TestMethod]
    public async Task Desktop_reopens_active_workspace_even_when_default_storage_is_missing_then_releases_on_exit()
    {
        var control = Path.Combine(_root, "control");
        var registry = new DesktopProfileRegistry(control, Path.Combine(control, "legacy"));
        var active = registry.Add("Active", Path.Combine(_root, "active"), true);
        var missing = registry.Add("Missing", Path.Combine(_root, "missing"), true);
        registry.MarkInitialized(missing.Id);
        registry.Update(missing.Id, missing.Name, true);
        using var owner = await InstanceProbe.StartAsync("desktop", control,
            new Dictionary<string, string?> { ["BAKABASE_TEST_PROFILE_ID"] = active.Id, ["BAKABASE_DATA_DIR"] = null });
        Assert.AreEqual("DESKTOP Entered", owner.FirstLine);
        Assert.AreEqual("PROFILE " + active.Id, await owner.ReadLineAsync(TimeSpan.FromSeconds(5)));
        using var duplicate = await InstanceProbe.StartAsync("desktop", control,
            new Dictionary<string, string?> { ["BAKABASE_TEST_PROFILE_ID"] = null, ["BAKABASE_DATA_DIR"] = null });
        Assert.AreEqual("DESKTOP Activated", duplicate.FirstLine);
        Assert.AreEqual("ACTIVATED SHOW", await owner.ReadLineAsync(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "missing")), "Reopen must not touch the disconnected default.");
        using var explicitLaunch = await InstanceProbe.StartAsync("desktop", control,
            new Dictionary<string, string?> { ["BAKABASE_TEST_PROFILE_ID"] = missing.Id });
        Assert.AreEqual("DESKTOP Activated", explicitLaunch.FirstLine);
        Assert.AreEqual("ACTIVATED WORKSPACE:" + missing.Id, await owner.ReadLineAsync(TimeSpan.FromSeconds(5)));
        owner.Stop();
        using var reopened = await InstanceProbe.StartAsync("desktop", control,
            new Dictionary<string, string?> { ["BAKABASE_TEST_PROFILE_ID"] = active.Id, ["BAKABASE_DATA_DIR"] = null });
        Assert.AreEqual("DESKTOP Entered", reopened.FirstLine);
    }

    [TestMethod]
    public async Task Running_profile_probe_detects_the_real_other_process_lock_without_writing_it()
    {
        using var owner = await InstanceProbe.StartAsync("hold", _root);
        Assert.AreEqual("LOCK Acquired", owner.FirstLine);
        Assert.ThrowsException<IOException>(() =>
        {
            using var probe = new FileStream(Path.Combine(_root, DataDirectoryLock.FileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        });
    }

    [TestMethod]
    public async Task Update_refuses_a_live_other_coordinator_and_accepts_its_normal_exit()
    {
        var installation = new DesktopInstallationInstances(_root);
        using var own = installation.Register(Environment.ProcessId);
        using var other = await InstanceProbe.StartAsync("installation", _root);
        StringAssert.StartsWith(other.FirstLine, "INSTALLATION ");
        Assert.ThrowsException<InvalidOperationException>(() => installation.AssertOnlyCoordinator(Environment.ProcessId));
        other.Stop();
        installation.AssertOnlyCoordinator(Environment.ProcessId);
    }
}
