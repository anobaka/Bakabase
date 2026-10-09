using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Reflection;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Service.Controllers;
using Bakabase.Service.Models.Input;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public sealed class FileStorageBoundaryTests
{
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), $"bakabase-storage-controller-{Guid.NewGuid():N}");
    private string Storage => Path.Combine(_temporary, "media");
    private string Outside => Path.Combine(_temporary, "outside");

    public FileStorageBoundaryTests()
    {
        Directory.CreateDirectory(Storage);
        Directory.CreateDirectory(Outside);
        File.WriteAllText(Path.Combine(Storage, "book.txt"), "inside");
        File.WriteAllText(Path.Combine(Outside, "secret.txt"), "outside");
    }

    private FileController Controller(bool restricted = true) => new(
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        null!, null!, null!, new Policy(Storage, restricted))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [TestMethod]
    public async Task RootListingsUseThePolicyAndReadonlyLocationsRemainSelectable()
    {
        var controller = Controller();
        var storage = controller.GetUserStorageRoots().Data!;
        Assert.IsTrue(storage.IsRestricted);
        Assert.IsTrue(storage.Roots.Single().ReadOnly == true);
        Assert.AreEqual(Storage, (await controller.Preview(null)).Data!.Entries.Single().Path);
        Assert.AreEqual(Storage, (await controller.SearchFileSystemEntries(null)).Data!.Single().Path);
        Assert.AreEqual(0, controller.ValidateUserStoragePaths(new UserStoragePathsInputModel
        {
            Paths = [Storage, Path.Combine(Storage, "future", "download")]
        }).Code);
    }

    [TestMethod]
    public async Task ExplicitBrowseAndManualPathsCannotEscape()
    {
        var controller = Controller();
        await Assert.ThrowsExactlyAsync<IOException>(() => controller.Preview(Outside));
        await Assert.ThrowsExactlyAsync<IOException>(() => controller.GetIwFsEntry(Path.Combine(Outside, "secret.txt")));
        Assert.ThrowsExactly<IOException>(() => controller.ValidateUserStoragePaths(new UserStoragePathsInputModel { Paths = [Outside] }));
        await Assert.ThrowsExactlyAsync<IOException>(() => controller.CreateDirectory(Outside));
    }

    [TestMethod]
    public async Task OutsideSymlinksAreHiddenDuringRecursiveReads()
    {
        if (OperatingSystem.IsWindows()) return; // Windows may require a symlink privilege.
        Directory.CreateSymbolicLink(Path.Combine(Storage, "escape"), Outside);
        var controller = Controller();
        Assert.IsFalse((await controller.Preview(Storage)).Data!.Entries.Any(entry => entry.Name == "escape"));
        Assert.IsFalse((await controller.SearchFileSystemEntries(null, Storage)).Data!.Any(entry => entry.Name == "escape"));
        Assert.IsFalse((await controller.GetAllFiles(Storage)).Data!.Any(path => path.Contains("secret.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(Outside, "secret.txt")));
    }

    [TestMethod]
    public async Task QueuedTaskPreflightRejectsOutsideSymlinksAndReportsItsStage()
    {
        if (OperatingSystem.IsWindows()) return;
        var source = Path.Combine(Storage, "source");
        var destination = Path.Combine(Storage, "destination");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        Directory.CreateDirectory(Path.Combine(destination, "source"));
        Directory.CreateSymbolicLink(Path.Combine(destination, "source", "nested"), Outside);
        var reported = new List<string>();
        var validate = typeof(FileController).GetMethod("EnsureStorageTreesInTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var work = (Task)validate.Invoke(Controller(), new object[]
        {
            new[] { source, Path.Combine(destination, "source") }, CancellationToken.None,
            (Func<string, Task>)(path => { reported.Add(path); return Task.CompletedTask; })
        })!;
        await Assert.ThrowsExactlyAsync<IOException>(() => work);
        Assert.IsTrue(reported.Count >= 2);
        Assert.IsTrue(File.Exists(Path.Combine(Outside, "secret.txt")));
    }

    [TestMethod]
    public async Task NativeBrowsingRemainsUnrestricted()
    {
        Assert.AreEqual(1, (await Controller(false).Preview(Outside)).Data!.Entries.Length);
        Assert.AreEqual(0, Controller(false).ValidateUserStoragePaths(new UserStoragePathsInputModel { Paths = [Outside] }).Code);
    }

    private sealed class Policy(string root, bool restricted) : IUserStoragePolicy
    {
        public bool IsRestricted => restricted;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            [new(root, "Media", "bind", true)];

        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            if (!restricted) return true;
            var full = Path.GetFullPath(path);
            var resolved = Directory.Exists(full) ? new DirectoryInfo(full).ResolveLinkTarget(true)?.FullName : null;
            var actual = resolved ?? full;
            return actual == root || actual.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }

        public void EnsureTreeMutationAllowed(string path) => EnsurePathAllowed(path);

        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            if (!IsPathAllowed(path, purpose)) throw new IOException("Choose a folder inside a mounted storage location.");
        }
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_temporary, true);
}
