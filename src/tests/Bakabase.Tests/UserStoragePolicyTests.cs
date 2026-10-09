using Bakabase.Abstractions.Exceptions;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;

namespace Bakabase.Tests;

[TestClass]
public sealed class UserStoragePolicyTests
{
    private const string RootMount = "1 0 0:1 / / rw,relatime - overlay overlay rw";
    private string _root = null!;
    private string _data = null!;
    private string _media = null!;
    private string _outside = null!;

    [TestInitialize]
    public void Initialize()
    {
        // /tmp and /var are aliases on macOS; mountinfo itself names the real location.
        _root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
            "bakabase-storage-policy-" + Guid.NewGuid().ToString("N"));
        _data = Directory.CreateDirectory(Path.Combine(_root, "appdata")).FullName;
        _media = Directory.CreateDirectory(Path.Combine(_root, "media")).FullName;
        _outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    private UserStoragePolicy Container(string mountInfo, string? metadata = null, Func<string?>? appData = null)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Linux mount namespaces are tested against a Unix filesystem.");
        return new(true, () => mountInfo, appData ?? (() => _data), () => metadata);
    }

    private static string Escape(string value) => value.Replace("\\", "\\134").Replace(" ", "\\040")
        .Replace("\t", "\\011").Replace("\n", "\\012");

    private static string Mounts(params (string Path, string FileSystem, string Options)[] entries) => RootMount + "\n" +
        string.Join("\n", entries.Select((entry, i) =>
            $"{i + 2} 1 8:1 /host {Escape(entry.Path)} {entry.Options} shared:2 - {entry.FileSystem} /dev/test rw"));

    private static string Metadata(params object[] mounts) => JsonSerializer.Serialize(new {schemaVersion = 1, mounts});

    [TestMethod]
    public void NativeServerHasReadyDrivesAndNoNewPathRestriction()
    {
        var policy = new UserStoragePolicy(isContainer: false,
            readMountInfo: () => throw new AssertFailedException("Native processes must not read Linux mountinfo."));

        Assert.IsFalse(policy.IsRestricted);
        foreach (var path in new[] {_data, _outside, "relative", ""})
        {
            Assert.IsTrue(policy.IsPathAllowed(path));
            policy.EnsurePathAllowed(path);
            policy.EnsureTreeMutationAllowed(path);
        }
        Assert.IsTrue(policy.GetRoots().Any(r => r.Path == Path.GetPathRoot(_root)));
        Assert.IsTrue(policy.GetRoots().All(r => r.StorageKind == "drive"));
    }

    [TestMethod]
    public void DirectDockerRunDiscoversDirectoryMountsAndAllowsReadOnlyStorageWithoutMetadata()
    {
        var policy = Container(Mounts((_data, "ext4", "rw"), (_media, "virtiofs", "ro")));

        Assert.IsTrue(policy.IsRestricted);
        var root = policy.GetRoots().Single();
        Assert.AreEqual(_media, root.Path);
        Assert.AreEqual("mount", root.StorageKind);
        Assert.AreEqual(true, root.ReadOnly);
        Assert.IsTrue(policy.IsPathAllowed(Path.Combine(_media, "new", "file.mp4")));
        policy.EnsurePathAllowed(Path.Combine(_media, "new"));
        policy.EnsureTreeMutationAllowed(_media);
        Assert.IsFalse(policy.IsPathAllowed(_data));
        Assert.IsTrue(policy.IsPathAllowed(_data, UserStoragePurpose.Setup));
        Assert.AreEqual(2, policy.GetRoots(UserStoragePurpose.Setup).Count);
    }

    [TestMethod]
    public void AppDataAncestorsRemainBrowsableButCannotBeMovedRenamedOrDeleted()
    {
        var parent = Directory.CreateDirectory(Path.Combine(_media, "application")).FullName;
        var appData = Directory.CreateDirectory(Path.Combine(parent, "appdata")).FullName;
        var sibling = Directory.CreateDirectory(Path.Combine(parent, "media")).FullName;
        var policy = Container(Mounts((_media, "ext4", "ro")), appData: () => appData);

        Assert.IsTrue(policy.IsPathAllowed(parent));
        Assert.IsTrue(policy.IsPathAllowed(Path.Combine(parent, "new-file.txt")));
        Assert.ThrowsExactly<UserStoragePathException>(() => policy.EnsureTreeMutationAllowed(_media));
        Assert.ThrowsExactly<UserStoragePathException>(() => policy.EnsureTreeMutationAllowed(parent));
        Assert.ThrowsExactly<UserStoragePathException>(() => policy.EnsureTreeMutationAllowed(appData));
        policy.EnsureTreeMutationAllowed(sibling);

        var alias = Path.Combine(_media, "application-alias");
        Directory.CreateSymbolicLink(alias, parent);
        Assert.IsTrue(policy.IsPathAllowed(alias));
        Assert.ThrowsExactly<UserStoragePathException>(() => policy.EnsureTreeMutationAllowed(alias));
        var aliasedAppData = Container(Mounts((_media, "ext4", "rw")),
            appData: () => Path.Combine(alias, "appdata"));
        Assert.ThrowsExactly<UserStoragePathException>(() => aliasedAppData.EnsureTreeMutationAllowed(parent));
        aliasedAppData.EnsureTreeMutationAllowed(sibling);
    }

    [TestMethod]
    public void ActualMountsOverrideMetadataAndUnmatchedMetadataCannotAuthorizeContainerFiles()
    {
        var policy = Container(Mounts((_media, "ext4", "ro")), Metadata(
            new {type = "volume", target = _media, readOnly = false},
            new {type = "bind", target = _outside, source = "/some-host-path", readOnly = false}));

        var root = policy.GetRoots().Single();
        Assert.AreEqual("volume", root.StorageKind);
        Assert.AreEqual(true, root.ReadOnly);
        Assert.IsTrue(policy.IsPathAllowed(_media));
        Assert.IsFalse(policy.IsPathAllowed(_outside));
        var temporary = Container(Mounts((_media, "tmpfs", "rw")),
            Metadata(new {type = "bind", target = _media, source = "/host"}));
        Assert.IsFalse(temporary.IsPathAllowed(_media));
        Assert.AreEqual(0, temporary.GetRoots().Count);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("{broken")]
    [DataRow("{\"schemaVersion\":2,\"mounts\":[]}")]
    [DataRow("{\"schemaVersion\":1,\"mounts\":[null]}")]
    public void OptionalMalformedMetadataDoesNotHideRealPersistentMounts(string? metadata)
    {
        var policy = Container(Mounts((_media, "nfs4", "rw")), metadata);

        Assert.IsTrue(policy.IsPathAllowed(_media));
        Assert.AreEqual("mount", policy.GetRoots().Single().StorageKind);
    }

    [TestMethod]
    public void RootFsSystemDirectoriesUnknownFilesystemsAndFileMountsAreExcluded()
    {
        var file = Path.Combine(_media, "mounted-file");
        File.WriteAllText(file, "fixture");
        var unknown = Directory.CreateDirectory(Path.Combine(_root, "unknown")).FullName;
        var policy = Container(Mounts((_media, "ext4", "rw"), (file, "ext4", "ro"),
            ("/etc", "ext4", "rw"), ("/dev", "tmpfs", "rw"), (unknown, "future-filesystem", "rw")));

        CollectionAssert.AreEqual(new[] {_media}, policy.GetRoots().Select(r => r.Path).ToArray());
        foreach (var denied in new[] {"/", "/etc", "/etc/passwd", "/dev", file, unknown, _outside,
                     _media + "-other", "relative", "/proc/self/mountinfo"})
            Assert.IsFalse(policy.IsPathAllowed(denied), denied);
        var error = Assert.ThrowsExactly<UserStoragePathException>(() => policy.EnsurePathAllowed(_outside));
        StringAssert.StartsWith(error.Message, "Choose a folder inside a mounted storage location.");
    }

    [TestMethod]
    public void NestedTmpfsMasksItsParentButAnActualNestedPersistentMountCanBeChosen()
    {
        var temporary = Directory.CreateDirectory(Path.Combine(_media, "temporary")).FullName;
        var persistent = Directory.CreateDirectory(Path.Combine(temporary, "mounted")).FullName;
        var policy = Container(Mounts((_media, "ext4", "rw"), (temporary, "tmpfs", "rw"),
            (persistent, "cifs", "ro")));

        Assert.IsTrue(policy.IsPathAllowed(Path.Combine(_media, "file")));
        Assert.IsFalse(policy.IsPathAllowed(Path.Combine(temporary, "file")));
        Assert.IsTrue(policy.IsPathAllowed(Path.Combine(persistent, "new", "file")));
        CollectionAssert.AreEquivalent(new[] {_media, persistent}, policy.GetRoots().Select(r => r.Path).ToArray());
    }

    [TestMethod]
    public void SymlinksCannotEscapeThroughAnAncestorFinalEntryOrDotDot()
    {
        var policy = Container(Mounts((_media, "ext4", "rw")));
        var destination = Directory.CreateDirectory(Path.Combine(_outside, "parent", "child")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(_media, "escape"), destination);
        File.WriteAllText(Path.Combine(_outside, "existing"), "fixture");
        File.CreateSymbolicLink(Path.Combine(_media, "file-link"), Path.Combine(_outside, "existing"));
        Directory.CreateSymbolicLink(Path.Combine(_media, "relative"), "../outside");
        Directory.CreateSymbolicLink(Path.Combine(_media, "dangling"), Path.Combine(_outside, "not-created"));

        foreach (var path in new[] {"escape/new/file", "escape/../secret", "file-link", "relative/file", "dangling/new"})
            Assert.IsFalse(policy.IsPathAllowed(Path.Combine(_media, path)), path);
        var within = Directory.CreateDirectory(Path.Combine(_media, "within")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(_media, "safe"), "within");
        Assert.IsTrue(policy.IsPathAllowed(Path.Combine(_media, "safe", "new", "file")));
        Assert.IsTrue(policy.IsPathAllowed(Path.Combine(within, "..", "file")));
    }

    [TestMethod]
    public void TemporaryEntriesCannotBecomePersistentByLinkingBackAndCyclesFailClosed()
    {
        var temporary = Directory.CreateDirectory(Path.Combine(_media, "temporary")).FullName;
        var policy = Container(Mounts((_media, "ext4", "rw"), (temporary, "tmpfs", "rw")));
        Directory.CreateSymbolicLink(Path.Combine(temporary, "back"), _media);
        Directory.CreateSymbolicLink(Path.Combine(_media, "loop-a"), "loop-b");
        Directory.CreateSymbolicLink(Path.Combine(_media, "loop-b"), "loop-a");

        Assert.IsFalse(policy.IsPathAllowed(Path.Combine(temporary, "back", "new")));
        Assert.IsFalse(policy.IsPathAllowed(Path.Combine(_media, "loop-a", "new")));
    }

    [TestMethod]
    public void EffectiveAppDataAndAliasesAreExcludedOnlyForUserFiles()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix symbolic-link fixture.");
        var actualData = Directory.CreateDirectory(Path.Combine(_media, "application-data")).FullName;
        var alias = Path.Combine(_root, "data-alias");
        Directory.CreateSymbolicLink(alias, actualData);
        var policy = Container(Mounts((_media, "ext4", "rw")), appData: () => alias);
        Directory.CreateSymbolicLink(Path.Combine(_media, "appdata-link"), actualData);

        Assert.AreEqual(_media, policy.GetRoots().Single().Path);
        foreach (var path in new[] {actualData, Path.Combine(actualData, "db"), Path.Combine(_media, "appdata-link", "db")})
        {
            Assert.IsFalse(policy.IsPathAllowed(path), path);
            Assert.IsTrue(policy.IsPathAllowed(path, UserStoragePurpose.Setup), path);
        }
    }

    [TestMethod]
    public void EscapedMountpointNamesAndDirectoryBoundariesArePreserved()
    {
        var path = Directory.CreateDirectory(Path.Combine(_root, "mounted space\\backslash")).FullName;
        var policy = Container(Mounts((path, "9p", "rw")));

        Assert.AreEqual(path, policy.GetRoots().Single().Path);
        Assert.IsTrue(policy.IsPathAllowed(Path.Combine(path, "file")));
        Assert.IsFalse(policy.IsPathAllowed(path + "-other/file"));
    }

    [TestMethod]
    public void MountpointsUnderExistingParentAliasesRetainTheirNamespacePath()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix mountpoint alias fixture.");
        var alias = Path.Combine(_root, "alias");
        Directory.CreateSymbolicLink(alias, _outside);
        Directory.CreateDirectory(Path.Combine(_outside, "mounted"));
        var mount = Path.Combine(alias, "mounted");
        var policy = Container(Mounts((mount, "ext4", "rw")));

        Assert.AreEqual(mount, policy.GetRoots().Single().Path);
        Assert.IsTrue(policy.IsPathAllowed(Path.Combine(mount, "new", "file")));
        Assert.IsFalse(policy.IsPathAllowed(Path.Combine(alias, "elsewhere")));
    }

    [TestMethod]
    public void UnreadableIncompleteOrAmbiguousMountTablesNeverFallBackToSlash()
    {
        foreach (var table in new[] {"", "not a mount table", Mounts((_media, "ext4", "rw")) + "\npartial",
                     "2 1 8:1 /host relative rw - ext4 /dev/test rw",
                     $"2 1 8:1 /host {Escape(_media)} rw - ext4 /dev/test rw"})
        {
            var policy = Container(table);
            Assert.AreEqual(0, policy.GetRoots().Count, table);
            Assert.IsFalse(policy.IsPathAllowed(_media), table);
        }
        var unreadable = new UserStoragePolicy(true, () => throw new IOException("mountinfo unavailable"), () => _data);
        Assert.AreEqual(0, unreadable.GetRoots().Count);
        Assert.IsFalse(unreadable.IsPathAllowed(_media));
        var stacked = Container(Mounts((_media, "ext4", "rw"), (_media, "tmpfs", "rw")));
        Assert.AreEqual(0, stacked.GetRoots().Count);
        Assert.IsFalse(stacked.IsPathAllowed(Path.Combine(_media, "child")));
    }

    [TestMethod]
    public void MountTableChangesRefreshThePolicyAndMissingAppDataFactsFailClosed()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Linux mount namespace fixture.");
        var table = Mounts((_media, "ext4", "rw"));
        var policy = new UserStoragePolicy(true, () => table, () => _data, () => null);
        Assert.IsTrue(policy.IsPathAllowed(_media));
        table = Mounts((_media, "tmpfs", "rw"));
        Assert.IsFalse(policy.IsPathAllowed(_media));
        Assert.AreEqual(0, policy.GetRoots().Count);
        var unknownData = new UserStoragePolicy(true, () => Mounts((_media, "ext4", "rw")));
        Assert.IsFalse(unknownData.IsPathAllowed(_media));
        Assert.AreEqual(0, unknownData.GetRoots().Count);
        Assert.IsTrue(unknownData.IsPathAllowed(_media, UserStoragePurpose.Setup));
    }
}
