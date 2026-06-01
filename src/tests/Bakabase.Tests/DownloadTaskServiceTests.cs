using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for the download-task management layer (no actual
/// downloading): task CRUD, the downloader-progress callbacks that mutate a
/// task row (OnProgress / OnNameAcquired / OnCheckpointReached), checkpoint
/// clearing, scoped deletion, and export.
/// </summary>
[TestClass]
public sealed class DownloadTaskServiceTests
{
    private DownloadTaskService _service = null!;

    [TestInitialize]
    public async Task Setup()
    {
        var sp = await TestServiceBuilder.BuildServiceProvider();
        // DownloadTaskService needs the concrete BakabaseLocalizer, which the test
        // harness does not register; build it from the IStringLocalizer it wraps.
        var localizer = new BakabaseLocalizer(
            sp.GetRequiredService<IStringLocalizer<Bakabase.InsideWorld.Business.SharedResource>>());
        // The downloader publishes a workflow.completed event on Complete. These tests
        // don't exercise workflow plumbing, so a no-op bus is enough.
        _service = new DownloadTaskService(sp, localizer, sp.GetRequiredService<IGuiAdapter>(),
            new NoopWorkflowEventBus());
    }

    private sealed class NoopWorkflowEventBus : IWorkflowEventBus
    {
        public Task PublishAsync<T>(string triggerKind, T payload, System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static DownloadTask NewTask(
        string key,
        ThirdPartyId thirdParty = ThirdPartyId.Bilibili,
        DownloadTaskStatus status = DownloadTaskStatus.Idle)
        => new()
        {
            Key = key,
            ThirdPartyId = thirdParty,
            Type = 0,
            DownloadPath = "/downloads/" + key,
            Status = status
        };

    private async Task<int> AddOne(string key, ThirdPartyId thirdParty = ThirdPartyId.Bilibili)
    {
        var rsp = await _service.AddRange([NewTask(key, thirdParty)]);
        return rsp.Data!.Single().Id;
    }

    #region CRUD

    [TestMethod]
    public async Task GetAllDto_EmptyWhenNoTasks()
        => Assert.AreEqual(0, (await _service.GetAllDto()).Length);

    [TestMethod]
    public async Task AddRange_PersistsTasks()
    {
        await _service.AddRange([NewTask("k1"), NewTask("k2")]);
        Assert.AreEqual(2, (await _service.GetAllDto()).Length);
    }

    [TestMethod]
    public async Task AddRange_AssignsIds()
    {
        var rsp = await _service.AddRange([NewTask("k1"), NewTask("k2")]);
        Assert.IsTrue(rsp.Data!.All(t => t.Id > 0));
    }

    [TestMethod]
    public async Task GetDto_ReturnsTaskByItsId()
    {
        var id = await AddOne("the-key");
        var dto = await _service.GetDto(id);
        Assert.AreEqual("the-key", dto.Key);
    }

    #endregion

    #region Progress callbacks

    [TestMethod]
    public async Task OnProgress_UpdatesProgress()
    {
        var id = await AddOne("k1");
        await _service.OnProgress(id, 0.5m);
        Assert.AreEqual(0.5m, (await _service.GetDto(id)).Progress);
    }

    [TestMethod]
    public async Task OnNameAcquired_UpdatesName()
    {
        var id = await AddOne("k1");
        await _service.OnNameAcquired(id, "Resolved Title");
        Assert.AreEqual("Resolved Title", (await _service.GetDto(id)).Name);
    }

    [TestMethod]
    public async Task OnCheckpointReached_UpdatesCheckpoint()
    {
        var id = await AddOne("k1");
        await _service.OnCheckpointReached(id, "page-5");
        Assert.AreEqual("page-5", (await _service.GetDto(id)).Checkpoint);
    }

    [TestMethod]
    public async Task ClearCheckpoints_NullsCheckpoints()
    {
        var id = await AddOne("k1");
        await _service.OnCheckpointReached(id, "page-5");
        await _service.ClearCheckpoints();
        Assert.IsNull((await _service.GetDto(id)).Checkpoint);
    }

    #endregion

    #region Deletion and export

    [TestMethod]
    public async Task Delete_ByIds_RemovesOnlyThoseTasks()
    {
        var id1 = await AddOne("k1");
        await AddOne("k2");
        await _service.Delete(new DownloadTaskDeleteInputModel([id1]));

        var remaining = await _service.GetAllDto();
        Assert.AreEqual(1, remaining.Length);
        Assert.AreEqual("k2", remaining[0].Key);
    }

    [TestMethod]
    public async Task Delete_ByThirdPartyId_ScopesDeletionToThatProvider()
    {
        var bilibiliId = await AddOne("bili", ThirdPartyId.Bilibili);
        var pixivId = await AddOne("pixiv", ThirdPartyId.Pixiv);

        // Both ids are passed, but the ThirdPartyId filter narrows it to Bilibili only.
        await _service.Delete(new DownloadTaskDeleteInputModel([bilibiliId, pixivId], ThirdPartyId.Bilibili));

        var remaining = await _service.GetAllDto();
        Assert.AreEqual(1, remaining.Length);
        Assert.AreEqual(ThirdPartyId.Pixiv, remaining[0].ThirdPartyId);
    }

    [TestMethod]
    public async Task Export_ProducesNonEmptyExcelBytes()
    {
        await _service.AddRange([NewTask("k1"), NewTask("k2")]);
        var bytes = await _service.Export();
        Assert.IsTrue(bytes.Length > 0);
    }

    #endregion
}
