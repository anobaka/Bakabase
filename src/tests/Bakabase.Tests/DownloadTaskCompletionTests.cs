using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Business.Migrations;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.TestKit.Utils;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadTaskCompletionTests
{
    private static readonly DateTime PreviousSuccess = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
    private IServiceProvider _services = null!;
    private DownloadTaskService _service = null!;
    private BakabaseDbContext _db = null!;
    private readonly SatisfiedPrecheck _precheck = new();

    public enum ScriptTaskType { Only = 1 }

    [ClassInitialize]
    public static void Register(TestContext _)
    {
        // No Downloader attribute: this synthetic producer must not join source discovery.
        DownloaderInternals.DownloaderTypeDefinitionMap.TryAdd(typeof(ScriptedDownloader), new DownloaderDefinition
        {
            ThirdPartyId = ThirdPartyId.ExHentai, TaskType = 1, EnumTaskType = ScriptTaskType.Only,
            Name = "Completion fixture", DownloaderType = typeof(ScriptedDownloader), HelperType = typeof(object),
            DefaultConvention = string.Empty
        });
    }

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.RemoveAll<IDownloadTaskPrecheck>();
            services.AddSingleton<IDownloadTaskPrecheck>(_precheck);
            services.AddSingleton<IWorkflowEventBus>(new NoopWorkflowBus());
            services.AddTransient(provider => new BakabaseLocalizer(provider.GetRequiredService<IStringLocalizer<SharedResource>>()));
            services.AddSingleton<DownloadQueuePump>(provider => new QuietPump(provider));
        });
        _service = _services.GetRequiredService<DownloadTaskService>();
        _db = _services.GetRequiredService<BakabaseDbContext>();
    }

    private async Task<int> Add(DateTime? completedAt = null, DownloadTaskStatus status = DownloadTaskStatus.Idle,
        long? interval = null)
    {
        var response = await _service.AddRange([new DownloadTask
        {
            Key = "completion-fixture", ThirdPartyId = ThirdPartyId.ExHentai, Type = 1,
            DownloadPath = "/downloads/completion-fixture", Status = status,
            CompletedAt = completedAt, Interval = interval
        }]);
        return response.Data!.Single().Id;
    }

    [TestMethod]
    public async Task HistoricalCompletedTasksWithoutATimeStayUnknownInBothDtoViews()
    {
        var id = await Add(status: DownloadTaskStatus.Complete);
        Assert.IsNull((await _service.GetDto(id)).CompletedAt);
        Assert.IsNull((await _service.GetAllDto()).Single().CompletedAt);
        Assert.IsNull((await _db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == id)).CompletedAt);
    }

    [TestMethod]
    public async Task SuccessfulCompletionIsPersistedAndSerializedAsAnUnambiguousUtcInstant()
    {
        var id = await Add();
        var finished = new DateTime(2026, 10, 1, 7, 8, 9, DateTimeKind.Utc);
        await _service.OnStatusChanged(id, new StatusDownloader(DownloaderStatus.Complete, finished), null);
        var dto = await _service.GetDto(id);
        Assert.AreEqual(finished, dto.CompletedAt);
        Assert.AreEqual(DateTimeKind.Utc, dto.CompletedAt!.Value.Kind);
        Assert.AreEqual(finished, (await _service.GetAllDto()).Single().CompletedAt);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, JsonSerializerOptions.Web));
        Assert.AreEqual("2026-10-01T07:08:09Z", json.RootElement.GetProperty("completedAt").GetString());

        await _service.OnProgress(id, 100);
        await _service.OnNameAcquired(id, "A completed title");
        await _service.OnCheckpointReached(id, "completed");
        Assert.AreEqual(finished, (await _service.GetDto(id)).CompletedAt);
    }

    [DataTestMethod]
    [DataRow(DownloaderStatus.Starting)]
    [DataRow(DownloaderStatus.Downloading)]
    [DataRow(DownloaderStatus.Failed)]
    [DataRow(DownloaderStatus.Stopped)]
    public async Task NonSuccessfulTransitionsPreserveThePreviousSuccessfulTime(DownloaderStatus status)
    {
        var id = await Add(PreviousSuccess, interval: 3600);
        await _service.OnStatusChanged(id, new StatusDownloader(status, DateTime.UtcNow), null);
        Assert.AreEqual(PreviousSuccess, (await _service.GetDto(id)).CompletedAt);
        Assert.AreEqual(PreviousSuccess, (await _db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == id)).CompletedAt);
    }

    [TestMethod]
    public async Task APeriodicTasksNextSuccessReplacesItsPreviousSuccessButAFailureDoesNot()
    {
        var id = await Add(PreviousSuccess, interval: 3600);
        await _service.OnStatusChanged(id, new StatusDownloader(DownloaderStatus.Failed), null);
        Assert.AreEqual(PreviousSuccess, (await _service.GetDto(id)).CompletedAt);
        var latest = PreviousSuccess.AddDays(2);
        await _service.OnStatusChanged(id, new StatusDownloader(DownloaderStatus.Complete, latest), null);
        Assert.AreEqual(latest, (await _service.GetDto(id)).CompletedAt);
        Assert.AreEqual(3600L, (await _service.GetDto(id)).Interval);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AnAlreadySatisfiedPrecheckDoesNotInventOrRefreshASuccessTime(bool knownTime)
    {
        var previous = knownTime ? PreviousSuccess : (DateTime?)null;
        var id = await Add(previous);
        _precheck.Satisfied = true;
        await _service.TryStartAllTasks(DownloadTaskStartMode.ManualStart, [id], DownloadTaskActionOnConflict.Ignore);
        var task = await _service.GetDto(id);
        Assert.AreEqual(DownloadTaskStatus.Complete, task.Status);
        Assert.AreEqual(previous, task.CompletedAt);
    }

    [TestMethod]
    public async Task AProgressSnapshotReadBeforeCompletionCannotOverwriteItsStatusOrTime()
    {
        var id = await Add();
        await using var progressScope = _services.CreateAsyncScope();
        var raced = new PausedReadService(progressScope.ServiceProvider,
            progressScope.ServiceProvider.GetRequiredService<BakabaseLocalizer>(), new NoopWorkflowBus());
        var progress = raced.OnProgress(id, 100);
        await raced.Read.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await _service.OnStatusChanged(id, new StatusDownloader(DownloaderStatus.Complete, PreviousSuccess), null);
        }
        finally { raced.Release.TrySetResult(); }
        await progress.WaitAsync(TimeSpan.FromSeconds(5));
        var task = await _service.GetDto(id);
        Assert.AreEqual(PreviousSuccess, task.CompletedAt);
        Assert.AreEqual(DownloadTaskStatus.Complete, task.Status);
        Assert.AreEqual(100m, task.Progress);
    }

    [TestMethod]
    public async Task ARealSuccessfulRunStampsItsTimeBeforePublishingComplete()
    {
        using var downloader = new ScriptedDownloader(_services, (_, _, _) => Task.CompletedTask);
        DateTime? published = null;
        downloader.OnStatusChanged += () =>
        {
            if (downloader.Status == DownloaderStatus.Complete) published = downloader.CompletedAt;
            return Task.CompletedTask;
        };
        var earliest = DateTime.UtcNow;
        await Run(downloader, new DownloadTask {Id = 17, Key = "fixture", DownloadPath = "/downloads"});
        Assert.AreEqual(DownloaderStatus.Complete, downloader.Status);
        Assert.AreEqual(downloader.CompletedAt, published);
        Assert.IsTrue(published >= earliest && published <= DateTime.UtcNow);
        Assert.AreEqual(DateTimeKind.Utc, published!.Value.Kind);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ARealFailedRunDoesNotAcquireANewCompletionTime(bool previousSuccess)
    {
        var previous = previousSuccess ? PreviousSuccess : (DateTime?)null;
        using var downloader = new ScriptedDownloader(_services, (_, _, _) => throw new InvalidDataException("Permanent fixture failure."));
        await Run(downloader, new DownloadTask {Id = 17, Key = "fixture", DownloadPath = "/downloads", CompletedAt = previous});
        Assert.AreEqual(DownloaderStatus.Failed, downloader.Status);
        Assert.AreEqual(previous, downloader.CompletedAt);
    }

    [TestMethod]
    public async Task ATransientRetryHasNoCompletionTimeUntilTheRetryActuallySucceeds()
    {
        using var downloader = new ScriptedDownloader(_services, (_, _, run) => run == 1
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "Synthetic connection interruption.")
            : Task.CompletedTask) {BlockRetry = true};
        var run = Run(downloader, new DownloadTask {Id = 17, Key = "fixture", DownloadPath = "/downloads"});
        await downloader.RetryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsNull(downloader.CompletedAt);
        downloader.RetryRelease.TrySetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, downloader.Runs);
        Assert.AreEqual(DownloaderStatus.Complete, downloader.Status);
        Assert.IsNotNull(downloader.CompletedAt);
    }

    [TestMethod]
    public async Task AStoppedRunKeepsItsLastSuccessfulTime()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var downloader = new ScriptedDownloader(_services, async (_, ct, _) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });
        await downloader.Start(new DownloadTask {Id = 17, Key = "fixture", DownloadPath = "/downloads", CompletedAt = PreviousSuccess});
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await downloader.StopAndWait(DownloaderStopBy.ManuallyStop);
        Assert.AreEqual(DownloaderStatus.Stopped, downloader.Status);
        Assert.AreEqual(PreviousSuccess, downloader.CompletedAt);
    }

    [TestMethod]
    public async Task MigrationAddsANullableColumnAndDoesNotBackfillHistoricalCompletedRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE DownloadTasks (Id INTEGER NOT NULL PRIMARY KEY, Status INTEGER NOT NULL);" +
                                "INSERT INTO DownloadTasks (Id, Status) VALUES (1, 300);";
            await setup.ExecuteNonQueryAsync();
        }
        await using var context = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite(connection).Options);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var migration = new AddDownloadTaskCompletedAt();
        foreach (var command in generator.Generate(migration.UpOperations, context.Model))
        {
            await using var sql = connection.CreateCommand();
            sql.CommandText = command.CommandText;
            await sql.ExecuteNonQueryAsync();
        }
        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT Status, CompletedAt FROM DownloadTasks WHERE Id = 1";
        await using var reader = await check.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(300L, reader.GetInt64(0));
        Assert.IsTrue(reader.IsDBNull(1));
    }

    private static async Task Run(ScriptedDownloader downloader, DownloadTask task)
    {
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        downloader.OnStatusChanged += () =>
        {
            if (downloader.Status is DownloaderStatus.Complete or DownloaderStatus.Failed or DownloaderStatus.Stopped)
                settled.TrySetResult();
            return Task.CompletedTask;
        };
        Assert.IsTrue(await downloader.Start(task));
        await settled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class ScriptedDownloader(IServiceProvider services, Func<DownloadTask, CancellationToken, int, Task> script)
        : AbstractDownloader<ScriptTaskType>(services)
    {
        public int Runs;
        public bool BlockRetry;
        public readonly TaskCompletionSource RetryEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource RetryRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;
        public override ScriptTaskType EnumTaskType => ScriptTaskType.Only;
        protected override Task StartCore(DownloadTask task, CancellationToken ct) => script(task, ct, Interlocked.Increment(ref Runs));
        protected override async Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken ct)
        {
            if (!BlockRetry) return;
            RetryEntered.TrySetResult();
            await RetryRelease.Task.WaitAsync(ct);
        }
    }

    private sealed class PausedReadService(IServiceProvider services, BakabaseLocalizer localizer, IWorkflowEventBus workflowBus)
        : DownloadTaskService(services, localizer, workflowBus)
    {
        private bool _pause = true;
        public readonly TaskCompletionSource Read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<DownloadTaskDbModel?> GetByKey(int id)
        {
            var task = await base.GetByKey(id);
            if (_pause)
            {
                _pause = false;
                Read.TrySetResult();
                await Release.Task;
            }
            return task;
        }
    }

    private sealed class QuietPump(IServiceProvider services) : DownloadQueuePump(services, NullLogger<DownloadQueuePump>.Instance)
    {
        protected override Task RunPassAsync() => Task.CompletedTask;
    }

    private sealed class SatisfiedPrecheck : IDownloadTaskPrecheck
    {
        public bool Satisfied;
        public ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;
        public Task<IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict>> EvaluateAsync(IReadOnlyList<DownloadTask> candidates, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict>>(candidates.ToDictionary(task => task.Id,
                _ => new DownloadTaskPrecheckVerdict(Satisfied ? DownloadTaskPrecheckOutcome.AlreadySatisfied : DownloadTaskPrecheckOutcome.Run)));
    }

    private sealed class StatusDownloader(DownloaderStatus status, DateTime? completedAt = null) : IDownloader
    {
        public ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;
        public int TaskType => 1;
        public DownloaderStatus Status => status;
        public DateTime? CompletedAt => completedAt;
        public string? Current => null;
        public double? EstimatedRemainingSeconds => null;
        public double? DownloadSpeedBytesPerSecond => null;
        public DownloaderStopBy? StoppedBy { get; set; } = DownloaderStopBy.ManuallyStop;
        public string? Message => null;
        public int FailureTimes => 0;
        public string? Checkpoint => null;
        public DateTime LastActivityAt => DateTime.Now;
        public Task Stop(DownloaderStopBy stopBy) => Task.CompletedTask;
        public Task<bool> Start(DownloadTask task) => Task.FromResult(true);
        public void ResetStatus() { }
        public void Dispose() { }
        public event Func<Task>? OnStatusChanged;
        public event Func<string, Task>? OnNameAcquired;
        public event Func<decimal, Task>? OnProgress;
        public event Func<Task>? OnDownloadSpeedChanged;
        public event Func<string, long, Task>? OnFileDownloaded;
        public event Func<Task>? OnCurrentChanged;
        public event Func<string, Task>? OnCheckpointChanged;
    }

    private sealed class NoopWorkflowBus : IWorkflowEventBus
    {
        public Task PublishAsync<T>(string triggerKind, T payload, CancellationToken ct = default) => Task.CompletedTask;
    }
}
