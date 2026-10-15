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
    public async Task Two_spaces_run_simultaneously_and_a_duplicate_hands_off_to_its_owner()
    {
        var control = Path.Combine(_root, "control");
        var registry = new DesktopProfileRegistry(control, Path.Combine(control, "legacy"));
        var first = registry.Add("A", Path.Combine(_root, "A"), true);
        var second = registry.Add("B", Path.Combine(_root, "B"), true);
        using var a = await InstanceProbe.StartAsync("profile", control, new Dictionary<string, string?> { ["BAKABASE_TEST_PROFILE_ID"] = first.Id });
        Assert.AreEqual("ENTRY Entered", a.FirstLine);
        StringAssert.Contains((await a.ReadLineAsync(TimeSpan.FromSeconds(5)))!, Path.Combine(_root, "A"));
        using var b = await InstanceProbe.StartAsync("profile", control, new Dictionary<string, string?> { ["BAKABASE_TEST_PROFILE_ID"] = second.Id });
        Assert.AreEqual("ENTRY Entered", b.FirstLine);
        StringAssert.Contains((await b.ReadLineAsync(TimeSpan.FromSeconds(5)))!, Path.Combine(_root, "B"));
        using var duplicate = await InstanceProbe.StartAsync("profile", control, new Dictionary<string, string?> { ["BAKABASE_TEST_PROFILE_ID"] = first.Id });
        Assert.AreEqual("ENTRY Refused", duplicate.FirstLine);
        Assert.AreEqual("ACTIVATED", await a.ReadLineAsync(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(b.HasExited);
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
