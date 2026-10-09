using Bakabase.Abstractions.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Abstractions.Models.Input;
using Newtonsoft.Json;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Abstractions;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Components;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Models;
using Bakabase.Service.Controllers;
using Bakabase.Service.Models.Input;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class UserStorageToolTests
{
    private string _root = null!;
    private string _mounted = null!;
    private string _outside = null!;
    private IUserStoragePolicy _container = null!;

    [TestInitialize]
    public void Setup()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The mountinfo fixture models a POSIX container namespace.");
        // macOS /var is a link. mountinfo represents the actual mounted directory, so use its
        // physical temporary root rather than introducing a host-only alias into this fixture.
        _root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
            "bakabase-storage-tools-" + Guid.NewGuid().ToString("N"));
        _mounted = Path.Combine(_root, "mounted");
        _outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(_mounted);
        Directory.CreateDirectory(_outside);
        var mount = _mounted.Replace("\\", "/").Replace(" ", "\\040");
        _container = new UserStoragePolicy(isContainer: true,
            readMountInfo: () => $"1 0 0:1 / / rw - overlay overlay rw\n20 1 0:20 / {mount} ro - ext4 /dev/fixture ro\n",
            appDataDirectory: () => Path.Combine(_root, "appdata"), readDeploymentMetadata: () => null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static FileNameModifierProcessInputModel Rename(string path) => new()
    {
        FilePaths = [path],
        Operations = [new FileNameModifierOperation
        {
            Operation = FileNameModifierOperationType.Insert, Position = FileNameModifierPosition.Start,
            Text = "renamed-", Target = FileNameModifierFileNameTarget.FileName
        }]
    };

    [TestMethod]
    public async Task DockerRejectsOutsideSourcesInPreviewAndExecutionWithoutMovingFiles()
    {
        var source = Path.Combine(_outside, "source.txt");
        await File.WriteAllTextAsync(source, "original");
        var controller = new FileNameModifierController(new FileNameModifier(), _container);
        await Assert.ThrowsExactlyAsync<UserStoragePathException>(() => controller.Preview(Rename(source)));
        var result = (await controller.Modify(Rename(source))).Data!.Single();
        Assert.IsFalse(result.Success);
        Assert.AreEqual("original", await File.ReadAllTextAsync(source));
        Assert.IsFalse(File.Exists(result.NewPath));
    }

    [TestMethod]
    public async Task DockerRejectsComputedOutsideDestinationBeforeWriting()
    {
        var source = Path.Combine(_mounted, "source.txt");
        var target = Path.Combine(_outside, "target.txt");
        await File.WriteAllTextAsync(source, "original");
        var controller = new FileNameModifierController(new FixedNameModifier(target), _container);
        await Assert.ThrowsExactlyAsync<UserStoragePathException>(() => controller.Preview(Rename(source)));
        var result = (await controller.Modify(Rename(source))).Data!.Single();
        Assert.IsFalse(result.Success);
        Assert.IsTrue(File.Exists(source));
        Assert.IsFalse(File.Exists(target));
    }

    [TestMethod]
    public async Task ReadOnlyMountRemainsSelectableAndPermissionIsLeftToTheFilesystem()
    {
        Assert.IsTrue(_container.GetRoots().Single().ReadOnly);
        var source = Path.Combine(_mounted, "source.txt");
        await File.WriteAllTextAsync(source, "original");
        var controller = new FileNameModifierController(new FileNameModifier(), _container);
        var preview = (await controller.Preview(Rename(source))).Data!.Single();
        // The fixture reports a read-only mount but the real host directory is writable. Success
        // proves there was no proactive read-only veto or write-permission probe in this flow.
        var result = (await controller.Modify(Rename(source))).Data!.Single();
        Assert.IsTrue(result.Success);
        Assert.AreEqual(preview, result.NewPath);
        Assert.AreEqual("original", await File.ReadAllTextAsync(preview));
    }

    [TestMethod]
    public async Task NativeRenameDoesNotRequireAnyMountedRoot()
    {
        var source = Path.Combine(_outside, "source.txt");
        await File.WriteAllTextAsync(source, "original");
        var controller = new FileNameModifierController(new FileNameModifier(),
            new UserStoragePolicy(isContainer: false, readMountInfo: () => throw new Exception("Must not read mountinfo")));
        var result = (await controller.Modify(Rename(source))).Data!.Single();
        Assert.IsTrue(result.Success);
        Assert.IsFalse(File.Exists(source));
        Assert.AreEqual("original", await File.ReadAllTextAsync(result.NewPath));
    }

    [TestMethod]
    public async Task PathMarkBatchChecksEveryPathBeforePersistingAndAllowsReadOnlyMounts()
    {
        var services = await TestServiceBuilder.BuildServiceProvider(s => s.AddSingleton(_container));
        try
        {
            var marks = services.GetRequiredService<IPathMarkService>();
            await Assert.ThrowsExactlyAsync<UserStoragePathException>(() => marks.AddRange([
                new PathMark {Path = _mounted, Type = PathMarkType.Resource, ConfigJson = "{}"},
                new PathMark {Path = _outside, Type = PathMarkType.Resource, ConfigJson = "{}"}
            ]));
            Assert.AreEqual(0, (await marks.GetAll()).Count);
            var saved = await marks.Add(new PathMark {Path = _mounted, Type = PathMarkType.Resource, ConfigJson = "{}"});
            Assert.IsTrue(saved.Id > 0);
            Assert.AreEqual(_mounted, (await marks.GetAll()).Single().Path);
        }
        finally { (services as IDisposable)?.Dispose(); }
    }

    [TestMethod]
    public async Task PathMarkUpdateAndMigrationRejectOutsideDestinationsWithoutChangingStoredPath()
    {
        var services = await TestServiceBuilder.BuildServiceProvider(s => s.AddSingleton(_container));
        try
        {
            var marks = services.GetRequiredService<IPathMarkService>();
            var saved = await marks.Add(new PathMark {Path = _mounted, Type = PathMarkType.Resource, ConfigJson = "{}"});
            saved.Path = _outside;
            await Assert.ThrowsExactlyAsync<UserStoragePathException>(() => marks.Update(saved));
            await Assert.ThrowsExactlyAsync<UserStoragePathException>(() => marks.MigratePath(_mounted, _outside));
            Assert.AreEqual(_mounted, (await marks.GetAll()).Single().Path);
            // An old unmounted prefix may be repaired to a mounted location; only the new path
            // is user storage. Requiring the old missing drive to be mounted prevents recovery.
            await marks.MigratePath(_outside, _mounted);
        }
        finally { (services as IDisposable)?.Dispose(); }
    }

    [TestMethod]
    public async Task PathMarkPreviewDoesNotTraverseDirectoryLinksOutsideMountedStorage()
    {
        var safe = Path.Combine(_mounted, "safe.txt");
        await File.WriteAllTextAsync(safe, "safe");
        await File.WriteAllTextAsync(Path.Combine(_outside, "outside.txt"), "outside");
        var link = Path.Combine(_mounted, "outside-link");
        Directory.CreateSymbolicLink(link, _outside);
        var services = await TestServiceBuilder.BuildServiceProvider(s => s.AddSingleton(_container));
        try
        {
            var marks = services.GetRequiredService<IPathMarkService>();
            foreach (var config in new[]
                     {
                         new ResourceMarkConfig {MatchMode = PathMatchMode.Regex, Regex = ".*", FsTypeFilter = PathFilterFsType.File},
                         new ResourceMarkConfig {MatchMode = PathMatchMode.Layer, Layer = 2, FsTypeFilter = PathFilterFsType.File}
                     })
            {
                var result = await marks.PreviewMatchedPaths(new PathMarkPreviewRequest
                {
                    Path = _mounted, Type = PathMarkType.Resource, ConfigJson = JsonConvert.SerializeObject(config)
                });
                Assert.IsFalse(result.Any(r => r.Path.Contains("outside")));
                if (config.MatchMode == PathMatchMode.Regex) Assert.IsTrue(result.Any(r => r.Path == safe));
            }
        }
        finally { (services as IDisposable)?.Dispose(); }
    }

    private sealed class FixedNameModifier(string target) : IFileNameModifier
    {
        public List<string> ModifyFileNames(List<string> fileNames, List<FileNameModifierOperation> operations) =>
            fileNames.Select(_ => target).ToList();
        public string PreviewModification(string fileName, List<FileNameModifierOperation> operations) => target;
        public bool ValidateOperation(FileNameModifierOperation operation) => true;
    }
}
