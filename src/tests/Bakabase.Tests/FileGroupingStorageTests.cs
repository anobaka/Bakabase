using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class FileGroupingStorageTests
{
    private string _root = null!;
    private string _mounted = null!;
    private string _outside = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
            "bakabase-grouping-storage-" + Guid.NewGuid().ToString("N"));
        _mounted = Directory.CreateDirectory(Path.Combine(_root, "mounted")).FullName;
        _outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    private UserStoragePolicy Container(string? appData = null)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The mount fixture models a Linux container.");
        var mount = _mounted.Replace("\\", "\\134").Replace(" ", "\\040");
        return new(true,
            () => $"1 0 0:1 / / rw - overlay overlay rw\n2 1 8:1 / {mount} ro - ext4 /dev/fixture ro",
            () => appData ?? Path.Combine(_root, "appdata"), () => null);
    }

    private static FileSystemEntryGroupingService Service(IUserStoragePolicy policy) =>
        new(NullLogger<FileSystemEntryGroupingService>.Instance, policy);

    private static FileSystemEntryGroupInputModel Model(string root) => new()
    {
        Paths = [root], GroupInternal = true,
        StrategyType = FileSystemEntryGroupStrategyType.KeyExtraction, KeyExtractionRegex = "(group)"
    };

    [TestMethod]
    public void PreviewAndSimilarityOnlyInspectImmediateCandidates()
    {
        var first = Directory.CreateDirectory(Path.Combine(_mounted, "group-a")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_mounted, "group-b")).FullName;
        File.WriteAllText(Path.Combine(first, "deep-file.txt"), "content is irrelevant to name grouping");
        Directory.CreateDirectory(Path.Combine(second, "deep-directory"));
        var policy = new ImmediatePolicy([_mounted, first, second]);
        var service = Service(policy);

        Assert.AreEqual(1, service.Preview(Model(_mounted)).Single().Groups.Length);
        Assert.IsTrue(service.ComputeSimilarityBreakpoints(Model(_mounted)).Count > 0);
        Assert.IsTrue(policy.Checked.All(path => path == _mounted || Path.GetDirectoryName(path) == _mounted));
    }

    [TestMethod]
    public void ExecutionRejectsAnEscapingTargetLinkWithoutMovingSourceFiles()
    {
        var policy = Container();
        var first = Path.Combine(_mounted, "group-a.txt");
        var second = Path.Combine(_mounted, "group-b.txt");
        File.WriteAllText(first, "a");
        File.WriteAllText(second, "b");
        Directory.CreateSymbolicLink(Path.Combine(_mounted, "group"), _outside);
        var service = Service(policy);
        var model = Model(_mounted);

        var preview = service.Preview(model);
        Assert.ThrowsExactly<UserStoragePathException>(() => service.Execute(model, preview));
        Assert.IsTrue(File.Exists(first));
        Assert.IsTrue(File.Exists(second));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_outside).Length);
    }

    [TestMethod]
    public void GroupingCannotRenameAnAncestorOfApplicationData()
    {
        var first = Directory.CreateDirectory(Path.Combine(_mounted, "group-a")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_mounted, "group-b")).FullName;
        var appData = Directory.CreateDirectory(Path.Combine(first, "appdata")).FullName;
        var service = Service(Container(appData));
        var model = Model(_mounted) with {GroupInternal = false, Paths = [first, second]};

        Assert.ThrowsExactly<UserStoragePathException>(() => service.Execute(model, service.Preview(model)));
        Assert.IsTrue(Directory.Exists(appData));
        Assert.IsFalse(Directory.Exists(Path.Combine(_mounted, "group")));
    }

    [TestMethod]
    public void ReadOnlyMountSelectionAndNativePathsAreNotWriteGated()
    {
        var container = Container();
        foreach (var (root, policy) in new (string, IUserStoragePolicy)[]
                 {(_mounted, container), (_outside, new UserStoragePolicy(isContainer: false))})
        {
            File.WriteAllText(Path.Combine(root, "group-a.txt"), "a");
            File.WriteAllText(Path.Combine(root, "group-b.txt"), "b");
            var service = Service(policy);
            var model = Model(root);
            service.Execute(model, service.Preview(model));
            Assert.IsTrue(File.Exists(Path.Combine(root, "group", "group-a.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "group", "group-b.txt")));
        }
    }

    private sealed class ImmediatePolicy(HashSet<string> allowed) : IUserStoragePolicy
    {
        public HashSet<string> Checked { get; } = [];
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            Checked.Add(path);
            if (!allowed.Contains(path)) throw new AssertFailedException("Grouping inspected a descendant: " + path);
            return true;
        }
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            IsPathAllowed(path, purpose);
        public void EnsureTreeMutationAllowed(string path) => EnsurePathAllowed(path);
    }
}
