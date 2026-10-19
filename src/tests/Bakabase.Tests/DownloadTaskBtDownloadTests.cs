using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Models.Constants;

namespace Bakabase.Tests;

public sealed partial class DownloadTaskDirectDownloadTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DownloadModeShortcut_RejectsWorkflowOwnedSourcesWithoutStoppingOrMutating(bool bt)
    {
        var id = await AddTask();
        await _manager.Start(await _service.GetDto(id), false);
        _db.DownloadResultOwners.Add(new DownloadResultOwnerDbModel
            {DownloadTaskId = id, AcquisitionTaskId = 1, WorkflowRunId = 2, ResourceId = 3});
        await _db.SaveChangesAsync();
        var before = await _service.GetDto(id);

        var response = bt ? await _service.BtDownload(id) : await _service.DirectDownload(id);

        Assert.AreEqual((int)ResponseCode.InvalidPayloadOrOperation, response.Code);
        StringAssert.Contains(response.Message!, "owned by a workflow");
        var after = await _service.GetDto(id);
        Assert.AreEqual(before.Options, after.Options);
        Assert.AreEqual(before.DownloadPath, after.DownloadPath);
        Assert.AreEqual(before.Status, after.Status);
        Assert.AreEqual(DownloaderStatus.Downloading, _manager[id]!.Status);
        Assert.IsFalse(_factory.Downloader.Draining.Task.IsCompleted);
        Assert.AreEqual(1, _factory.Downloader.StartedOptions.Count);
    }

    [TestMethod]
    [DataRow(ExHentaiDownloadTaskType.SingleWork)]
    [DataRow(ExHentaiDownloadTaskType.List)]
    [DataRow(ExHentaiDownloadTaskType.Watched)]
    public async Task BtDownload_RestartsTheSameTaskWithStrictBtOptionsAndPreservesItsDestination(ExHentaiDownloadTaskType type)
    {
        var id = await AddTask(type, status: DownloadTaskStatus.Complete);
        var before = await _service.GetDto(id);
        await _service.UpdateByKey(id, task => task.Options = """
            {"DownloadMode":3,"PreferTorrent":false,"FallbackToDirect":true,
             "NoTorrentCheckedAt":"2026-10-19T00:00:00Z","downloadResultWorkflowId":19,
             "torrentDownloadedAt":"2026-09-20T00:00:00Z","futureOption":{"keep":7}}
            """);
        var response = await _service.BtDownload(id);
        Assert.AreEqual((int)ResponseCode.Success, response.Code);
        var after = await _service.GetDto(id);
        Assert.AreEqual(before.Id, after.Id);
        Assert.AreEqual(before.Key, after.Key);
        Assert.AreEqual(before.DownloadPath, after.DownloadPath);
        Assert.AreEqual(before.Interval, after.Interval);
        Assert.AreEqual(before.StartPage, after.StartPage);
        Assert.AreEqual(before.EndPage, after.EndPage);
        Assert.AreEqual(before.AutoRetry, after.AutoRetry);
        Assert.AreEqual(type == ExHentaiDownloadTaskType.SingleWork ? before.Checkpoint : null, after.Checkpoint);
        var typed = after.GetTypedOptions<ExHentaiTaskOptions>();
        Assert.AreEqual(ExHentaiDownloadMode.Torrent, typed.EffectiveDownloadMode);
        Assert.IsTrue(typed.PreferTorrent);
        Assert.IsFalse(typed.FallbackToDirect);
        Assert.IsNull(typed.NoTorrentCheckedAt);
        Assert.AreEqual(19, typed.DownloadResultWorkflowId);
        Assert.IsNotNull(typed.TorrentDownloadedAt);
        using var json = JsonDocument.Parse(after.Options!);
        Assert.AreEqual(7, json.RootElement.GetProperty("futureOption").GetProperty("keep").GetInt32());
        Assert.IsFalse(json.RootElement.TryGetProperty("DownloadMode", out _));
        Assert.IsFalse(json.RootElement.TryGetProperty("FallbackToDirect", out _));
        Assert.AreEqual(1, _factory.Downloader.StartedOptions.Count);
        Assert.AreEqual(ExHentaiDownloadMode.Torrent, JsonSerializer.Deserialize<ExHentaiTaskOptions>(
            _factory.Downloader.StartedOptions[0]!, JsonSerializerOptions.Web)!.EffectiveDownloadMode);
    }

    [TestMethod]
    public async Task BtDownload_DrainsAnActiveRunBeforeChangingItsModeAndIgnoresStaleStarts()
    {
        var id = await AddTask();
        await _service.UpdateByKey(id, t => t.Options = "{\"downloadMode\":1}");
        var oldSnapshot = await _service.GetDto(id);
        await _manager.Start(oldSnapshot, false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.Downloader.Drain = release.Task;
        var action = _service.BtDownload(id);
        await _factory.Downloader.Draining.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(action.IsCompleted);
        Assert.AreEqual(ExHentaiDownloadMode.Direct, (await _service.GetDto(id)).GetTypedOptions<ExHentaiTaskOptions>().EffectiveDownloadMode);
        var staleStart = _manager.Start(oldSnapshot, false);
        release.SetResult();
        Assert.AreEqual((int)ResponseCode.Success, (await action.WaitAsync(TimeSpan.FromSeconds(5))).Code);
        await staleStart.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, _factory.Downloader.StartedOptions.Count);
        Assert.AreEqual(ExHentaiDownloadMode.Torrent, JsonSerializer.Deserialize<ExHentaiTaskOptions>(
            _factory.Downloader.StartedOptions[^1]!, JsonSerializerOptions.Web)!.EffectiveDownloadMode);
    }

    [TestMethod]
    public async Task BtDownload_UsesTheExistingConflictPolicy()
    {
        var occupied = await AddTask();
        var target = await AddTask(status: DownloadTaskStatus.Failed);
        await _manager.Start(await _service.GetDto(occupied), false);
        var response = await _service.BtDownload(target);
        Assert.AreEqual((int)ResponseCode.Conflict, response.Code);
        Assert.AreEqual(DownloaderStatus.Downloading, _manager[occupied]!.Status);
        Assert.IsFalse((await _service.GetDto(target)).GetTypedOptions<ExHentaiTaskOptions>().FallbackToDirect);
        Assert.AreEqual((int)ResponseCode.Success,
            (await _service.BtDownload(target, DownloadTaskActionOnConflict.StopOthers)).Code);
        Assert.AreEqual(DownloaderStatus.Stopped, _manager[occupied]!.Status);
        Assert.AreEqual(DownloaderStatus.Downloading, _manager[target]!.Status);
    }

    [TestMethod]
    public async Task BtDownload_RejectsMissingAndNonExHentaiTasks()
    {
        Assert.AreEqual((int)ResponseCode.NotFound, (await _service.BtDownload(999999)).Code);
        var id = await AddTask(thirdParty: ThirdPartyId.Bilibili);
        var before = (await _service.GetDto(id)).Options;
        Assert.AreEqual((int)ResponseCode.InvalidPayloadOrOperation, (await _service.BtDownload(id)).Code);
        Assert.AreEqual(before, (await _service.GetDto(id)).Options);
        Assert.AreEqual(0, _factory.Downloader.StartedOptions.Count);
    }
}
