using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public sealed class DLsiteDefaultDirectoryTests
{
    private string _root = null!;
    private IServiceProvider _services = null!;
    private IDLsiteWorkService _works = null!;
    private DLsiteOptions _source = null!;
    private DownloaderGlobalOptions _global = null!;

    private sealed class Storage(string root) : IUserStoragePolicy
    {
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            [new(root, "storage", "volume", false)];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            if (!IsPathAllowed(path, purpose)) throw new UserStoragePathException("Outside mounted storage.");
        }
        public void EnsureTreeMutationAllowed(string path) => EnsurePathAllowed(path);
    }

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-dlsite-defaults-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _services = await TestServiceBuilder.BuildServiceProvider(s => s.AddSingleton<IUserStoragePolicy>(new Storage(_root)));
        _works = _services.GetRequiredService<IDLsiteWorkService>();
        _source = _services.GetRequiredService<IBOptions<DLsiteOptions>>().Value;
        _global = _services.GetRequiredService<IBOptions<DownloaderGlobalOptions>>().Value;
        _global.DefaultDownloadPath = Path.Combine(_root, "Downloads");
        await _works.AddOrUpdate(new DLsiteWorkDbModel {WorkId = "RJ000001", Title = "Fixture"});
    }

    [TestCleanup]
    public void Cleanup()
    {
        (_services as IDisposable)?.Dispose();
        Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task PreparingWorkUsesGlobalDLsiteFolderAndPreservesUnsetSourceOptions()
    {
        var path = await _works.PrepareDownloadDirectory("RJ000001");
        Assert.AreEqual(Path.Combine(_root, "Downloads", "DLsite", "RJ000001"), path);
        Assert.IsTrue(Directory.Exists(path));
        Assert.AreEqual(path, (await _works.GetByWorkId("RJ000001"))!.LocalPath);
        Assert.IsNull(_source.DefaultPath);
    }

    [TestMethod]
    public async Task PreparingWorkUsesConfiguredSourceDirectoryAheadOfGlobalDirectory()
    {
        _source.DefaultPath = Path.Combine(_root, "SourceOverride");
        var path = await _works.PrepareDownloadDirectory("RJ000001");
        Assert.AreEqual(Path.Combine(_root, "SourceOverride", "RJ000001"), path);
        Assert.IsFalse(Directory.Exists(_global.DefaultDownloadPath));
    }

    [TestMethod]
    public async Task ExtractingDownloadedWorkUsesTheSameInheritedDirectory()
    {
        var path = Path.Combine(_root, "Downloads", "DLsite", "RJ000001");
        Directory.CreateDirectory(path);
        using (var zip = ZipFile.Open(Path.Combine(path, "fixture.zip"), ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("readme.txt").Open());
            await writer.WriteAsync("downloaded content");
        }
        await _works.ExtractWork("RJ000001");
        Assert.AreEqual("downloaded content", await File.ReadAllTextAsync(Path.Combine(path, "content", "readme.txt")));
        var work = (await _works.GetByWorkId("RJ000001"))!;
        Assert.IsTrue(work.IsDownloaded);
        Assert.AreEqual(path, work.LocalPath);
        Assert.IsNull(_source.DefaultPath);
    }

    [TestMethod]
    public async Task MissingDefaultsKeepPreparationAndExtractionBlocked()
    {
        _global.DefaultDownloadPath = null;
        await Assert.ThrowsExactlyAsync<Exception>(() => _works.PrepareDownloadDirectory("RJ000001"));
        await Assert.ThrowsExactlyAsync<BTaskException>(() => _works.ExtractWork("RJ000001"));
        Assert.AreEqual(0, Directory.GetDirectories(_root).Length);
        Assert.IsNull((await _works.GetByWorkId("RJ000001"))!.LocalPath);
    }

    [TestMethod]
    public async Task AllDLsiteFileOperationsValidateTheResolvedContainerDirectoryBeforeWriting()
    {
        _source.Accounts = [new DLsiteAccount {Name = "fixture", Cookie = "session=fixture"}];
        var outside = Path.Combine(Path.GetTempPath(), "bakabase-dlsite-outside-" + Guid.NewGuid().ToString("N"));
        _global.DefaultDownloadPath = outside;
        await Assert.ThrowsExactlyAsync<UserStoragePathException>(() => _works.PrepareDownloadDirectory("RJ000001"));
        await Assert.ThrowsExactlyAsync<UserStoragePathException>(() => _works.DownloadWork("RJ000001"));
        await Assert.ThrowsExactlyAsync<UserStoragePathException>(() => _works.ExtractWork("RJ000001"));
        Assert.IsFalse(Directory.Exists(outside));
        Assert.IsNull((await _works.GetByWorkId("RJ000001"))!.LocalPath);

        _global.DefaultDownloadPath = Path.Combine(_root, "Downloads");
        _source.DefaultPath = outside;
        await Assert.ThrowsExactlyAsync<UserStoragePathException>(() => _works.PrepareDownloadDirectory("RJ000001"));
        Assert.IsFalse(Directory.Exists(outside));
    }

    [TestMethod]
    public async Task DLsiteDefaultPreviewUsesTheSameRuleWithoutNeedingADownloaderHelper()
    {
        var controller = new DownloadTaskController(null!, null!, null!, null!, null!);
        var dlsite = _services.GetRequiredService<IBOptions<DLsiteOptions>>();
        var global = _services.GetRequiredService<IBOptions<DownloaderGlobalOptions>>();
        var response = await controller.GetDownloaderOptions(ThirdPartyId.DLsite, dlsite, global);
        Assert.IsNull(response.Data!.DefaultPath);
        Assert.AreEqual(Path.Combine(_root, "Downloads", "DLsite"), response.Data.EffectiveDefaultPath);
        Assert.IsNull(_source.DefaultPath);
        _source.DefaultPath = Path.Combine(_root, "Custom");
        response = await controller.GetDownloaderOptions(ThirdPartyId.DLsite, dlsite, global);
        Assert.AreEqual(_source.DefaultPath, response.Data!.DefaultPath);
        Assert.AreEqual(_source.DefaultPath, response.Data.EffectiveDefaultPath);
    }
}
