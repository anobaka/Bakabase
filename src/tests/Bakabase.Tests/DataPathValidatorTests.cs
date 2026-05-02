using System;
using System.IO;
using System.Runtime.InteropServices;
using Bakabase.Infrastructures.Components.App.Relocation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class DataPathValidatorTests
{
    private string _root = null!;

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-validator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static OSPlatform Host =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX :
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows :
        OSPlatform.Linux;

    private DataPathValidator.Input MakeInput(
        string target,
        string current,
        OSPlatform? platform = null,
        long minFreeBytes = 0,
        long freeBytes = long.MaxValue,
        bool canWrite = true,
        string? installRoot = null)
    {
        return new DataPathValidator.Input
        {
            TargetPath = target,
            CurrentDataDir = current,
            Platform = platform ?? Host,
            MinFreeBytes = minFreeBytes,
            CanWrite = _ => canWrite,
            GetFreeSpaceBytes = _ => freeBytes,
            FindVelopackInstallRoot = _ => installRoot,
        };
    }

    [TestMethod]
    public void Validate_RelativePath_Refused()
    {
        var input = MakeInput("relative/path", _root);
        var result = DataPathValidator.Validate(input);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(DataPathValidator.RefusalReason.RelativePath, result.Reason);
    }

    [TestMethod]
    public void Validate_SameAsCurrent_Refused()
    {
        var input = MakeInput(_root, _root);
        var result = DataPathValidator.Validate(input);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(DataPathValidator.RefusalReason.SameAsCurrent, result.Reason);
    }

    [TestMethod]
    public void Validate_TargetIsAncestorOfCurrent_Refused()
    {
        var current = Path.Combine(_root, "current", "AppData");
        Directory.CreateDirectory(current);
        var input = MakeInput(_root, current);
        var result = DataPathValidator.Validate(input);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(DataPathValidator.RefusalReason.CircularContainment, result.Reason);
    }

    [TestMethod]
    public void Validate_CurrentIsAncestorOfTarget_Refused()
    {
        var target = Path.Combine(_root, "child");
        var input = MakeInput(target, _root);
        var result = DataPathValidator.Validate(input);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(DataPathValidator.RefusalReason.CircularContainment, result.Reason);
    }

    [TestMethod]
    public void Validate_InsideVelopackInstall_Refused()
    {
        // Inside current/ — atomically replaced on upgrade, must be refused.
        var installRoot = Path.Combine(_root, "Bakabase-install");
        var target = Path.Combine(installRoot, "current", "AppData");
        var current = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(current);
        var input = MakeInput(target, current, installRoot: installRoot);
        var result = DataPathValidator.Validate(input);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(DataPathValidator.RefusalReason.InsideInstall, result.Reason);
    }

    [TestMethod]
    public void Validate_InsideVelopackPackages_Refused()
    {
        // packages/ holds Velopack download cache — pruned on upgrade, must be refused.
        var installRoot = Path.Combine(_root, "Bakabase-install");
        var target = Path.Combine(installRoot, "packages", "data");
        var current = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(current);
        var input = MakeInput(target, current, installRoot: installRoot);
        var result = DataPathValidator.Validate(input);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(DataPathValidator.RefusalReason.InsideInstall, result.Reason);
    }

    [TestMethod]
    public void Validate_TargetEqualsInstallRoot_Allowed()
    {
        // Install root itself — survives upgrade (Velopack only swaps current/), so it is
        // not refused. This is the recovery target for users coming from 2.3.0-beta.69~74
        // whose default DataPath physically lived at the install root. The orthogonal
        // uninstall / Repair risk is surfaced via AppInfo.DataInInstallRoot.
        var installRoot = Path.Combine(_root, "Bakabase-install");
        Directory.CreateDirectory(installRoot);
        var current = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(current);
        var input = MakeInput(installRoot, current, installRoot: installRoot);
        var result = DataPathValidator.Validate(input);
        Assert.IsTrue(result.Valid, $"Expected valid, got {result.Reason}");
    }

    [TestMethod]
    public void Validate_InstallRootSiblingDir_Allowed()
    {
        // A non-Velopack-managed dir under the install root (e.g. user-stored data
        // folder at the install root level) is also safe from upgrade and must be
        // allowed.
        var installRoot = Path.Combine(_root, "Bakabase-install");
        var target = Path.Combine(installRoot, "my-data");
        Directory.CreateDirectory(target);
        var current = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(current);
        var input = MakeInput(target, current, installRoot: installRoot);
        var result = DataPathValidator.Validate(input);
        Assert.IsTrue(result.Valid, $"Expected valid, got {result.Reason}");
    }

    [TestMethod]
    public void Validate_SystemPath_Windows_Refused()
    {
        // Pure-platform test: synthetic Windows paths so the test is host-independent.
        var input = MakeInput(@"C:\Windows\Temp\foo", @"D:\elsewhere\bakabase", OSPlatform.Windows);
        var result = DataPathValidator.Validate(input);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(DataPathValidator.RefusalReason.SystemPath, result.Reason);
    }

    [TestMethod]
    public void Validate_SystemPath_Linux_Refused()
    {
        var input = MakeInput("/usr/local/share/bakabase", "/home/foo/data", OSPlatform.Linux);
        var result = DataPathValidator.Validate(input);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(DataPathValidator.RefusalReason.SystemPath, result.Reason);
    }

    [TestMethod]
    public void Validate_NoWritePermission_Refused()
    {
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(current);
        var target = Path.Combine(_root, "target");
        var input = MakeInput(target, current, canWrite: false);
        var result = DataPathValidator.Validate(input);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(DataPathValidator.RefusalReason.NoWritePermission, result.Reason);
    }

    [TestMethod]
    public void Validate_TargetEmpty_StateIsNeedsCopy()
    {
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(current);
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var input = MakeInput(target, current);
        var result = DataPathValidator.Validate(input);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual(DataPathValidator.TargetState.NeedsCopy, result.State);
    }

    [TestMethod]
    public void Validate_TargetDoesNotExist_StateIsNeedsCopy()
    {
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(current);
        var target = Path.Combine(_root, "target-not-yet");
        var input = MakeInput(target, current);
        var result = DataPathValidator.Validate(input);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual(DataPathValidator.TargetState.NeedsCopy, result.State);
    }

    [TestMethod]
    public void Validate_TargetHasUnrelatedFiles_StateIsNeedsCopy()
    {
        // With merge semantics, an empty target and a target full of unrelated files behave
        // identically — the runner merges into either. Both surface as NeedsCopy.
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(current);
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "random.txt"), "hello");
        var input = MakeInput(target, current);
        var result = DataPathValidator.Validate(input);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual(DataPathValidator.TargetState.NeedsCopy, result.State);
    }

    [TestMethod]
    public void Validate_TargetHasAppJson_StateIsHasBakabaseData_VersionParsed()
    {
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(current);
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "app.json"),
            """{"App":{"Version":"2.3.0-beta.65","Language":"en-US"}}""");
        var input = MakeInput(target, current);
        var result = DataPathValidator.Validate(input);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual(DataPathValidator.TargetState.HasBakabaseData, result.State);
        Assert.AreEqual("2.3.0-beta.65", result.TargetAppVersion);
    }

    [TestMethod]
    public void Validate_TargetHasDb_StateIsHasBakabaseData()
    {
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(current);
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "bakabase_insideworld.db"), "");
        var input = MakeInput(target, current);
        var result = DataPathValidator.Validate(input);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual(DataPathValidator.TargetState.HasBakabaseData, result.State);
    }

    [TestMethod]
    public void Validate_TargetHasConfigsDir_StateIsHasBakabaseData()
    {
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(current);
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(Path.Combine(target, "configs"));
        var input = MakeInput(target, current);
        var result = DataPathValidator.Validate(input);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual(DataPathValidator.TargetState.HasBakabaseData, result.State);
    }

    [TestMethod]
    public void Validate_InsufficientFreeSpace_Refused()
    {
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(current);
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var input = MakeInput(target, current, minFreeBytes: 1_000_000_000, freeBytes: 1_000);
        var result = DataPathValidator.Validate(input);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(DataPathValidator.RefusalReason.InsufficientSpace, result.Reason);
        Assert.AreEqual(1_000, result.FreeSpaceBytes);
    }

    [TestMethod]
    public void Validate_SkipsFreeSpaceCheckWhenMinIsZero()
    {
        // MinFreeBytes=0 is the "don't check" sentinel — handy for tests and for callers who
        // want to defer the precise check to copy time.
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(current);
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var input = MakeInput(target, current, minFreeBytes: 0, freeBytes: 1);
        var result = DataPathValidator.Validate(input);
        Assert.IsTrue(result.Valid);
    }

    [TestMethod]
    public void IsSystemPath_LinuxIsCaseSensitive()
    {
        Assert.IsFalse(DataPathValidator.IsSystemPath("/USR/local", OSPlatform.Linux));
        Assert.IsTrue(DataPathValidator.IsSystemPath("/usr/local", OSPlatform.Linux));
    }

    [TestMethod]
    public void IsSystemPath_WindowsIsCaseInsensitive()
    {
        Assert.IsTrue(DataPathValidator.IsSystemPath(@"c:\windows\temp", OSPlatform.Windows));
        Assert.IsTrue(DataPathValidator.IsSystemPath(@"C:\WINDOWS\Temp", OSPlatform.Windows));
    }

    [TestMethod]
    public void IsSystemPath_BoundaryNotFalseMatched()
    {
        Assert.IsFalse(DataPathValidator.IsSystemPath("/usrFoo", OSPlatform.Linux));
        Assert.IsFalse(DataPathValidator.IsSystemPath("/var2", OSPlatform.Linux));
    }
}
