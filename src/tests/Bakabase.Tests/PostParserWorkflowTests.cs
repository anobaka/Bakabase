using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParserTask = Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.PostParserTask;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostParserWorkflowTests
{
    private IServiceProvider _services = null!;
    private FakeReader _reader = null!;
    private FakeExtractor _extractor = null!;
    private FakePurchaser _purchaser = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _reader = new();
        _extractor = new();
        _purchaser = new(_reader);
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.AddSingleton<IPostContentService>(_reader);
            services.AddSingleton<IPostDownloadInfoExtractor>(_extractor);
            services.RemoveAll<ISharedContentPurchaser>();
            services.AddSingleton<ISharedContentPurchaser>(_purchaser);
        });
        _services.GetRequiredService<IBOptions<ThirdPartyOptions>>().Value.AutomaticallyParsingPosts = false;
        await using var scope = _services.CreateAsyncScope();
        var providers = scope.ServiceProvider.GetRequiredService<IAiProviderService>();
        var provider = await providers.AddAsync(new() {Kind = AiProviderKind.OpenAI, Name = "fixture", LlmEnabled = true});
        await scope.ServiceProvider.GetRequiredService<IAiFeatureService>().SaveConfigAsync(new AiFeatureConfigDbModel
        {
            Feature = AiFeature.PostParser, ProviderConfigId = provider.Id, ModelId = "fixture", UseDefault = false
        });
    }

    private async Task<int> Add(string link = "https://example.test/post/1", PostParserSource source = 0)
    {
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        await service.AddRange(new() {[source] = [link]}, [PostParseTarget.DownloadInfo]);
        return (await service.GetAll()).Single(t => t.Link == link).Id;
    }

    private async Task Dispatch()
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>().DispatchAsync();
    }

    private async Task<ParserTask> TaskState(int id)
    {
        await using var scope = _services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().GetAll()).Single(t => t.Id == id);
    }

    private async Task<WorkflowRunDbModel> RunState(int id)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().Set<WorkflowRunDbModel>().AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task Execute(int id)
    {
        await using var scope = _services.CreateAsyncScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<WorkflowRunner<BakabaseDbContext>>().ExecuteAsync(id,
                new BTaskArgs(new PauseToken(), CancellationToken.None, new BTask("fixture", () => "fixture"),
                    _ => Task.CompletedTask, _services));
        }
        catch (OperationCanceledException) { }
        await _services.GetRequiredService<BTaskManager>().Clean($"workflow.run.{id}");
    }

    [TestMethod]
    public async Task ConcurrentDispatchAndRepeatedAddShareOneRun()
    {
        var id = await Add();
        var revision = (await TaskState(id)).Revision;
        await Task.WhenAll(Dispatch(), Dispatch(), Dispatch());
        var first = await TaskState(id);
        await Add();
        await Dispatch();
        var second = await TaskState(id);
        Assert.AreEqual(revision, second.Revision);
        Assert.AreEqual(first.WorkflowRunId, second.WorkflowRunId);
        await using var scope = _services.CreateAsyncScope();
        Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().Set<WorkflowRunDbModel>().CountAsync());
        await Execute(first.WorkflowRunId!.Value);
        var done = await TaskState(id);
        Assert.AreEqual(WorkflowRunStatus.Success, done.WorkflowStatus);
        Assert.AreEqual("Extracted title", done.Title);
        Assert.AreEqual("secret", done.Results![PostParseTarget.DownloadInfo]!["resources"]![0]!["password"]!.GetValue<string>());
        Assert.AreEqual(1, _reader.Reads);
    }

    [TestMethod]
    public async Task FailedExtractionRetriesSameRunFromSavedContent()
    {
        _extractor.FailuresRemaining = 1;
        var id = await Add();
        await Dispatch();
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Failed, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1, (await RunState(runId)).CurrentStepIndex);
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().Retry(id);
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(runId, (await TaskState(id)).WorkflowRunId);
        Assert.AreEqual(1, _reader.Reads);
        Assert.AreEqual(2, _extractor.Extractions);
    }

    [TestMethod]
    public async Task ReparseDuringExtractionRejectsOldCompletion()
    {
        _extractor.Hold = true;
        var id = await Add();
        await Dispatch();
        var old = await TaskState(id);
        var executing = Execute(old.WorkflowRunId!.Value);
        await _extractor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().ReParse(id);
        await Dispatch();
        var fresh = await TaskState(id);
        _extractor.Hold = false;
        _extractor.Release.TrySetResult();
        await executing;
        Assert.IsNull((await TaskState(id)).Results);
        Assert.AreNotEqual(old.WorkflowRunId, fresh.WorkflowRunId);
        Assert.IsTrue(fresh.Revision > old.Revision);
        await Execute(fresh.WorkflowRunId!.Value);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
    }

    [TestMethod]
    public async Task DeleteDuringReadingDoesNotExtractOrResurrect()
    {
        _reader.Hold = true;
        var id = await Add();
        await Dispatch();
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        var executing = Execute(runId);
        await _reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().Delete(id);
        _reader.Release.TrySetResult();
        await executing;
        var deleted = await TaskState(id);
        Assert.IsTrue(deleted.IsDeleted);
        Assert.IsNull(deleted.Results);
        Assert.AreEqual(0, _extractor.Extractions);
        Assert.AreEqual(0, _purchaser.Purchases);
        await Dispatch();
        Assert.AreEqual(runId, (await TaskState(id)).WorkflowRunId);
    }

    [TestMethod]
    public async Task LostQueuedTaskReusesCommittedRun()
    {
        var id = await Add();
        await Dispatch();
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        await _services.GetRequiredService<BTaskManager>().Clean($"workflow.run.{runId}");
        await Dispatch();
        Assert.AreEqual(runId, (await TaskState(id)).WorkflowRunId);
        Assert.AreEqual(1, _services.GetRequiredService<BTaskManager>().Tasks.Count(t => t.Id == $"workflow.run.{runId}"));
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
    }

    [TestMethod]
    public async Task RestartResumesFromCheckpointWithoutReadingAgain()
    {
        _extractor.FailuresRemaining = 1;
        var id = await Add();
        await Dispatch();
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        await Execute(runId);
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            await db.Set<WorkflowRunDbModel>().Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Running));
            await scope.ServiceProvider.GetRequiredService<WorkflowRunRehydrator<BakabaseDbContext>>().MarkInterruptedRunsAsync();
            await scope.ServiceProvider.GetRequiredService<WorkflowRunRehydrator<BakabaseDbContext>>().ReEnqueuePendingRunsAsync();
        }
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1, _reader.Reads);
    }

    [TestMethod]
    public async Task GenericTextWorkflowDoesNotRequireTaskResourceOrReader()
    {
        await using var scope = _services.CreateAsyncScope();
        var definition = await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>().SeedAsync();
        var run = await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>().RunManuallyAsync(definition,
            JsonSerializer.Serialize(new PostParserInput {Text = "shared link and password", Title = "Pasted"}, WorkflowJson.Options));
        await Execute(run.Id);
        Assert.AreEqual(WorkflowRunStatus.Success, (await RunState(run.Id)).Status);
        using var output = JsonDocument.Parse((await RunState(run.Id)).OutputItemsJson!);
        Assert.AreEqual("Extracted title", output.RootElement[0].GetProperty("result").GetProperty("title").GetString());
        Assert.AreEqual(0, _reader.Reads);
        Assert.AreEqual(1, _extractor.Extractions);
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().Set<PostParserTaskDbModel>().CountAsync());
    }

    [TestMethod]
    public async Task GenericWorkflowCannotUseTheSavedTasksPurchaseLimit()
    {
        _reader.Locks = [new("https://example.test/lock", 1, false)];
        await using var scope = _services.CreateAsyncScope();
        var definition = await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>().SeedAsync();
        var run = await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>().RunManuallyAsync(definition,
            JsonSerializer.Serialize(new PostParserInput {Link = "https://example.test/post", SourceHint = "SoulPlus"}, WorkflowJson.Options));
        await Execute(run.Id);
        Assert.AreEqual(WorkflowRunStatus.Failed, (await RunState(run.Id)).Status);
        Assert.AreEqual(0, _purchaser.Purchases);
        Assert.AreEqual(0, _extractor.Extractions);
    }

    [TestMethod]
    public async Task SavedSoulPlusTaskKeepsConfiguredPurchaseThreshold()
    {
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 10;
        _reader.Locks = [new("https://example.test/lock", 5, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        await Execute((await TaskState(id)).WorkflowRunId!.Value);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1, _purchaser.Purchases);
        Assert.AreEqual(2, _reader.Reads);
    }

    [TestMethod]
    public async Task UnknownPriceNeverPurchasesOrExtractsPartialContent()
    {
        _reader.Locks = [new("https://example.test/lock", null, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        await Execute((await TaskState(id)).WorkflowRunId!.Value);
        Assert.AreEqual(WorkflowRunStatus.Failed, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(0, _purchaser.Purchases);
        Assert.AreEqual(0, _extractor.Extractions);
    }

    [TestMethod]
    public async Task LegacyResultsAndUserscriptDeleteStatusesRemainAvailable()
    {
        var id = await Add(source: PostParserSource.SoulPlus);
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            await db.Set<PostParserTaskDbModel>().Where(t => t.Id == id).ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.Results, "{\"DownloadInfo\":{\"resources\":[]}}"));
        }
        await Dispatch();
        Assert.IsNull((await TaskState(id)).WorkflowRunId);
        await using var check = _services.CreateAsyncScope();
        var service = check.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        var link = "https://example.test/post/1";
        Assert.AreEqual(PostParserTaskStatus.Complete, (await service.GetStatusesByLinks(PostParserSource.SoulPlus, [link]))[link]);
        Assert.AreEqual(1, await service.DeleteByLinks(PostParserSource.SoulPlus, [link]));
        Assert.AreEqual(PostParserTaskStatus.Deleted, (await service.GetStatusesByLinks(PostParserSource.SoulPlus, [link]))[link]);
    }

    [TestMethod]
    public async Task ManualTriggerRejectsTaskImpersonationAndAmbiguousInput()
    {
        var trigger = new PostParserManualTrigger();
        Assert.IsFalse(trigger.Matches(new PostParserInput {Text = "input"}, null));
        Assert.ThrowsException<InvalidOperationException>(() => trigger.BuildManualPayload(null, "{\"taskId\":1,\"text\":\"input\"}"));
        Assert.ThrowsException<InvalidOperationException>(() => trigger.BuildManualPayload(null, "{\"text\":\"input\",\"link\":\"https://example.test\"}"));
        await Task.CompletedTask;
    }

    private sealed class FakeReader : IPostContentService
    {
        public int Reads;
        public bool Hold;
        public List<PostContentLock> Locks = [];
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CanRead(string reference, string? sourceHint = null) => true;
        public async Task<PostContent> ReadAsync(string reference, string? sourceHint = null, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Reads);
            Entered.TrySetResult();
            if (Hold) await Release.Task.WaitAsync(ct);
            return new() {Title = "Post title", MainHtml = "shared content", SourceHint = sourceHint, Locks = Locks.ToList()};
        }
    }

    private sealed class FakeExtractor : IPostDownloadInfoExtractor
    {
        public int Extractions;
        public int FailuresRemaining;
        public bool Hold;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<PostDownloadInfo> ExtractAsync(PostContent content, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Extractions);
            Entered.TrySetResult();
            if (Hold) await Release.Task.WaitAsync(ct);
            if (FailuresRemaining-- > 0) throw new InvalidOperationException("fixture extraction failure");
            return new() {Title = "Extracted title", Resources = [new() {Link = "https://example.test/file.zip", Code = "code", Password = "secret"}]};
        }
    }

    private sealed class FakePurchaser(FakeReader reader) : ISharedContentPurchaser
    {
        public PostParserSource Source => PostParserSource.SoulPlus;
        public int Purchases;
        public Task BuyAsync(string lockUrl, CancellationToken ct)
        {
            Purchases++;
            reader.Locks = reader.Locks.Select(l => l with {IsBought = true}).ToList();
            return Task.CompletedTask;
        }
    }
}
