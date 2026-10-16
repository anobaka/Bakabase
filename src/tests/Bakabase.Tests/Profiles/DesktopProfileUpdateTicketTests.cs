using System;
using System.Collections.Generic;
using System.IO;
using Bakabase.Infrastructures.Components.App.Profiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Profiles;

[TestClass]
public class DesktopProfileUpdateTicketTests
{
    private string _root = null!;
    private readonly HashSet<DesktopInstallationInstances.ProcessIdentity> _alive = [];
    private bool _updaterAtPath;
    private static readonly DesktopInstallationInstances.ProcessIdentity Owner = new(101, 1000);
    private static readonly DesktopInstallationInstances.ProcessIdentity Coordinator = new(102, 2000);
    private static readonly DesktopInstallationInstances.ProcessIdentity Updater = new(103, 3000);
    [TestInitialize] public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-update-ticket-" + Guid.NewGuid().ToString("N"));
        _alive.Clear(); _updaterAtPath = false;
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private DesktopInstallationInstances Instances(string build = "old") => new(_root, build, p => _alive.Contains(p), _ => _updaterAtPath);
    private DesktopInstallationInstances.UpdateReservation Reserve()
    {
        _alive.Add(Owner); _alive.Add(Coordinator);
        return Instances().Reserve(Coordinator.Id, Path.Combine(_root, "Update.exe"), Owner, Coordinator);
    }

    [TestMethod] public void New_launch_is_refused_during_preparation_and_after_child_and_coordinator_exit()
    {
        var reservation = Reserve();
        Assert.ThrowsException<InvalidOperationException>(() => Instances().Register(999));
        _alive.Add(Updater);
        reservation.RecordUpdater(Updater);
        _alive.Remove(Owner); _alive.Remove(Coordinator);
        Assert.ThrowsException<InvalidOperationException>(() => Instances().Register(999));
        _alive.Remove(Updater);
        using var recovered = Instances().Register(999);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "update.json")));
    }

    [TestMethod] public void Upgraded_binary_can_launch_even_while_the_updater_is_finishing_restart()
    {
        var reservation = Reserve();
        _alive.Add(Updater);
        reservation.RecordUpdater(Updater);
        using var upgraded = Instances("new").Register(999);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "update.json")));
    }

    [TestMethod] public void Preparation_failure_leaves_no_permanent_launch_lock()
    {
        var reservation = Reserve();
        reservation.Cancel();
        using var recovered = Instances().Register(999);
    }

    [TestMethod] public void Spawn_before_pid_write_crash_refuses_launch_until_the_exact_updater_exits()
    {
        var reservation = Reserve();
        _updaterAtPath = true;
        _alive.Clear();
        reservation.ReleaseLease(); // abrupt owner exit, updater already started
        Assert.ThrowsException<InvalidOperationException>(() => Instances().Register(999));
        _updaterAtPath = false;
        using var recovered = Instances().Register(999);
    }

    [TestMethod] public void Reused_pid_is_not_mistaken_for_the_old_updater()
    {
        var reservation = Reserve();
        reservation.RecordUpdater(Updater);
        _alive.Clear();
        _alive.Add(Updater with { StartedAt = Updater.StartedAt + 1 });
        using var recovered = Instances().Register(999);
    }
}
