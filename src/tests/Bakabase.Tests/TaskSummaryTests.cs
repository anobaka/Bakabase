using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Bakabase.Tests;

[TestClass]
public sealed class TaskSummaryTests
{
    private static PostParserTask Complete() => new()
    {
        Source = PostParserSource.SoulPlus,
        Link = "https://www.north-plus.net/read.php?tid=1",
        Targets = [PostParseTarget.DownloadInfo],
        ParsingState = "complete",
        Results = new() {[PostParseTarget.DownloadInfo] = JsonNode.Parse("{\"isComplete\":true,\"resources\":[]}")}
    };

    [TestMethod]
    [DataRow(WorkflowRunStatus.Pending, 0, 0)]
    [DataRow(WorkflowRunStatus.Running, 0, 0)]
    [DataRow(WorkflowRunStatus.Success, 1, 0)]
    [DataRow(WorkflowRunStatus.Failed, 0, 1)]
    [DataRow(WorkflowRunStatus.Cancelled, 0, 1)]
    [DataRow(WorkflowRunStatus.Interrupted, 0, 0)]
    [DataRow(WorkflowRunStatus.Waiting, 0, 0)]
    public void WorkflowStateTakesPrecedenceOverSavedCompleteResults(WorkflowRunStatus status, int complete, int failed)
    {
        var task = Complete() with {WorkflowRunId = 7, WorkflowStatus = status};
        var summary = PostParserTaskSummary.Create([task]);
        Assert.AreEqual(1, summary.Total);
        Assert.AreEqual(complete, summary.Completed);
        Assert.AreEqual(failed, summary.Failed);
    }

    [TestMethod]
    public void ActiveWorkflowDoesNotReportSavedSuccessOrFailure()
    {
        var task = Complete() with {WorkflowRunId = 7, WorkflowStatus = WorkflowRunStatus.Success, Error = "old error"};
        var summary = PostParserTaskSummary.Create([task], activeTasks: new HashSet<string> {"workflow.run.7"});
        Assert.AreEqual(0, summary.Completed);
        Assert.AreEqual(0, summary.Failed);
        Assert.AreEqual(1, summary.Total);
    }

    [TestMethod]
    [DataRow("awaitingAi")]
    [DataRow("awaitingPurchase")]
    [DataRow("possiblyExpired")]
    [DataRow("partial")]
    [DataRow("snapshotSaved")]
    public void IncompleteParsingStatesAreNotSuccesses(string state)
    {
        var task = Complete() with {WorkflowRunId = 7, WorkflowStatus = WorkflowRunStatus.Success, ParsingState = state};
        Assert.AreEqual(0, PostParserTaskSummary.Create([task]).Completed);
    }

    [TestMethod]
    public void PartialDownloadInformationIsNotCompleteEvenAfterWorkflowSuccess()
    {
        var task = Complete() with {WorkflowRunId = 7, WorkflowStatus = WorkflowRunStatus.Success};
        task.Results![PostParseTarget.DownloadInfo] = JsonNode.Parse("{\"isComplete\":false,\"resources\":[]}");
        Assert.AreEqual(0, PostParserTaskSummary.Create([task]).Completed);
    }

    [TestMethod]
    public void LegacyResultWrappersAndTargetErrorsMatchTheParserPage()
    {
        var complete = Complete() with {ParsingState = null};
        complete.Results![PostParseTarget.DownloadInfo] = JsonNode.Parse("{\"data\":{\"resources\":[]},\"parsedAt\":\"2026-10-10\"}");
        var failed = Complete() with {ParsingState = null, Results = new()
        {
            [PostParseTarget.DownloadInfo] = JsonNode.Parse("{\"error\":\"Extraction failed\"}")
        }};
        var summary = PostParserTaskSummary.Create([complete, failed]);
        Assert.AreEqual(1, summary.Completed);
        Assert.AreEqual(1, summary.Failed);
    }

    [TestMethod]
    public void LockedPostContentIsNotComplete()
    {
        var task = Complete() with
        {
            WorkflowRunId = 7, WorkflowStatus = WorkflowRunStatus.Success,
            ContentSnapshot = new() {Locks = [new("https://www.north-plus.net/job.php?buy=1", 1, false)]}
        };
        Assert.AreEqual(0, PostParserTaskSummary.Create([task]).Completed);
    }

    [TestMethod]
    public async Task ParserSummaryExcludesDeletedAndOtherSourcesFromTheDatabase()
    {
        var sp = await TestServiceBuilder.BuildServiceProvider(services => services.AddTransient(provider =>
            new BakabaseLocalizer(provider.GetRequiredService<IStringLocalizer<SharedResource>>())));
        try
        {
            var db = sp.GetRequiredService<BakabaseDbContext>();
            db.PostParserTasks.AddRange(
                new PostParserTaskDbModel {Source = PostParserSource.SoulPlus, Link = "https://www.north-plus.net/read.php?tid=1", ParsingState = "complete"},
                new PostParserTaskDbModel {Source = PostParserSource.SoulPlus, Link = "https://www.south-plus.net/read.php?tid=2", Error = "failed"},
                new PostParserTaskDbModel {Source = PostParserSource.SoulPlus, Link = "https://www.north-plus.net/read.php?tid=3", IsDeleted = true, ParsingState = "complete"},
                new PostParserTaskDbModel {Source = 0, Link = "https://example.test/post", ParsingState = "complete"});
            await db.SaveChangesAsync();
            var parser = sp.GetRequiredService<IPostParserTaskService>();
            var selected = await parser.GetSummary(PostParserSource.SoulPlus);
            Assert.AreEqual(2, selected.Total);
            Assert.AreEqual(1, selected.Completed);
            Assert.AreEqual(1, selected.Failed);
            var all = await parser.GetSummary();
            Assert.AreEqual(3, all.Total);
            Assert.AreEqual(2, all.Completed);
        }
        finally { if (sp is IAsyncDisposable disposable) await disposable.DisposeAsync(); }
    }

    [TestMethod]
    public void SummaryJsonContainsOnlyTheThreeCounts()
    {
        var json = JsonSerializer.Serialize(new TaskSummary {Completed = 3, Failed = 2, Total = 7}, JsonSerializerOptions.Web);
        Assert.AreEqual("{\"completed\":3,\"failed\":2,\"total\":7}", json);
    }

    [TestMethod]
    [DataRow("/download-task/summary?thirdPartyId=2")]
    [DataRow("/post-parser/task/summary?source=5")]
    public async Task HttpSummaryUsesTheRequestedSourceAndReturnsOnlyCounts(string path)
    {
        var sp = await TestServiceBuilder.BuildServiceProvider(services => services.AddTransient(provider =>
            new BakabaseLocalizer(provider.GetRequiredService<IStringLocalizer<SharedResource>>())));
        try
        {
            var db = sp.GetRequiredService<BakabaseDbContext>();
            db.DownloadTasks.AddRange(
                new DownloadTaskDbModel {Key = "eh-complete", ThirdPartyId = ThirdPartyId.ExHentai, Status = DownloadTaskDbModelStatus.Complete},
                new DownloadTaskDbModel {Key = "eh-failed", ThirdPartyId = ThirdPartyId.ExHentai, Status = DownloadTaskDbModelStatus.Failed},
                new DownloadTaskDbModel {Key = "other-complete", ThirdPartyId = ThirdPartyId.Pixiv, Status = DownloadTaskDbModelStatus.Complete});
            db.PostParserTasks.AddRange(
                new PostParserTaskDbModel {Source = PostParserSource.SoulPlus, Link = "https://www.north-plus.net/read.php?tid=1", ParsingState = "complete"},
                new PostParserTaskDbModel {Source = PostParserSource.SoulPlus, Link = "https://www.north-plus.net/read.php?tid=2", Error = "failed"},
                new PostParserTaskDbModel {Source = PostParserSource.SoulPlus, Link = "https://www.north-plus.net/read.php?tid=3", ParsingState = "complete", IsDeleted = true},
                new PostParserTaskDbModel {Source = 0, Link = "https://example.test/post", ParsingState = "complete"});
            await db.SaveChangesAsync();
            await using var host = await ServiceGateHost.StartAsync(
                [typeof(DownloadTaskController), typeof(PostParserTaskSummaryController)], services =>
                {
                    services.AddSingleton(sp.GetRequiredService<DownloadTaskService>());
                    services.AddSingleton(sp.GetRequiredService<DownloadRecordService>());
                    services.AddSingleton(sp.GetRequiredService<IBOptions<ExHentaiOptions>>());
                    services.AddSingleton(sp.GetRequiredService<IStringLocalizer<SharedResource>>());
                    services.AddSingleton(sp.GetRequiredService<IDownloaderFactory>());
                    services.AddSingleton(sp.GetRequiredService<IPostParserTaskService>());
                });
            host.Remote.Mode = RemoteAccessMode.Unrestricted;
            using var request = host.Request(HttpMethod.Get, path);
            request.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.3.100");
            using var response = await host.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.AreEqual(0, json.RootElement.GetProperty("code").GetInt32());
            Assert.AreEqual("{\"completed\":1,\"failed\":1,\"total\":2}", json.RootElement.GetProperty("data").GetRawText());
        }
        finally { if (sp is IAsyncDisposable disposable) await disposable.DisposeAsync(); }
    }
}
