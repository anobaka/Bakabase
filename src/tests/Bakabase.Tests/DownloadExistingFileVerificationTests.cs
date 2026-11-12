using System.Security.Cryptography;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadExistingFileVerificationTests
{
    private string _root = null!;
    private BakabaseDbContext _db = null!;
    private DownloadResultService _results = null!;
    private DownloadTaskDbModel _task = null!;
    private CloudExistingDownloadVerification _proof = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "existing-download-verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=" + Path.Combine(_root, "test.db")).Options);
        await _db.Database.EnsureCreatedAsync();
        _results = new(_db, () => Path.Combine(_root, "appdata"));
        var directory = Path.Combine(_root, "download"); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "data.bin"); var bytes = "verified content"u8.ToArray();
        await File.WriteAllBytesAsync(file, bytes);
        _task = new() {Key = "original-source", DownloadPath = directory, Status = DownloadTaskDbModelStatus.Failed,
            Progress = 100, Message = "original manifest registration failure", Options = "original request", ThirdPartyId = ThirdPartyId.BaiduNetdisk};
        _db.DownloadTasks.Add(_task); await _db.SaveChangesAsync();
        _proof = new([new(file, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant())]);
    }
    [TestCleanup]
    public async Task Cleanup() {await _db.DisposeAsync(); Directory.Delete(_root, true);}

    [TestMethod]
    public async Task VerifiedContentRestoresSameTaskAndResultAtomicallyWithoutChangingOriginalErrorOrRequest()
    {
        var before = await _db.DownloadTasks.AsNoTracking().SingleAsync();
        await _results.RecordVerifiedExistingFilesAsync(before, _proof, _ => Task.CompletedTask, default);
        var after = await _db.DownloadTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(DownloadTaskDbModelStatus.Complete, after.Status); Assert.IsNotNull(after.CompletedAt);
        Assert.AreEqual(before.Options, after.Options); Assert.AreEqual(before.Message, after.Message);
        Assert.AreEqual(before.DownloadPath, after.DownloadPath); Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        var result = await _db.DownloadResults.SingleAsync(); Assert.AreEqual(before.Id, result.DownloadTaskId);
        Assert.IsTrue(await _results.CanReuseAsync(result)); Assert.IsTrue(File.Exists(_proof.Files.Single().Path));
    }

    [DataTestMethod]
    [DataRow("corrupt")]
    [DataRow("truncated")]
    [DataRow("missingHash")]
    [DataRow("pathOutside")]
    public async Task HundredPercentAndExpectedLengthCannotReplaceContentProof(string mode)
    {
        var proof = _proof; var original = await _db.DownloadTasks.AsNoTracking().SingleAsync();
        if (mode == "corrupt") await File.WriteAllTextAsync(proof.Files[0].Path, "changed content!");
        if (mode == "truncated") await File.WriteAllTextAsync(proof.Files[0].Path, "short");
        if (mode == "missingHash") proof = new([proof.Files[0] with {Sha256 = ""}]);
        if (mode == "pathOutside") proof = proof with {PayloadRootDirectory = _root};
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _results.RecordVerifiedExistingFilesAsync(original, proof, _ => Task.CompletedTask, default));
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
        var after = await _db.DownloadTasks.AsNoTracking().SingleAsync(); Assert.AreEqual(DownloadTaskDbModelStatus.Failed, after.Status);
        Assert.IsNull(after.CompletedAt); Assert.IsTrue(File.Exists(_proof.Files[0].Path));
    }

    [TestMethod]
    public async Task RequestEditedWhileVerifyingCannotRegisterOldResultOrCompleteTask()
    {
        var original = await _db.DownloadTasks.AsNoTracking().SingleAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _results.RecordVerifiedExistingFilesAsync(original, _proof,
            async ct => {await _db.DownloadTasks.Where(t => t.Id == original.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Options, "user edit"), ct);}, default));
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
        var after = await _db.DownloadTasks.AsNoTracking().SingleAsync(); Assert.AreEqual("user edit", after.Options);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, after.Status);
    }
}
