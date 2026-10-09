using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Pixiv;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Steps;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Acquisition;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class UserStorageConsumerTests
{
    private string _root = null!;
    private string _mounted = null!;
    private string _internal = null!;
    private IServiceProvider _services = null!;
    private StoragePolicy _policy = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-storage-consumers-" + Guid.NewGuid().ToString("N"));
        _mounted = Path.Combine(_root, "mounted");
        _internal = Path.Combine(_root, "internal-work");
        Directory.CreateDirectory(_mounted);
        Directory.CreateDirectory(_internal);
        _policy = new StoragePolicy(_mounted);
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
            services.AddSingleton<IUserStoragePolicy>(_policy));
    }

    [TestCleanup]
    public void Cleanup()
    {
        (_services as IDisposable)?.Dispose();
        Directory.Delete(_root, true);
    }

    private sealed class StoragePolicy(string root) : IUserStoragePolicy
    {
        public bool DenyAll { get; set; }
        public bool IsRestricted => true;
        // ReadOnly is deliberately true: selection must not perform permission probes or reject it.
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            [new(root, "mounted", "bind", true)];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            !DenyAll && !string.IsNullOrWhiteSpace(path) && (Path.GetFullPath(path) == root ||
                        Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        public void EnsureTreeMutationAllowed(string path) => EnsurePathAllowed(path);
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            if (!IsPathAllowed(path, purpose)) throw new IOException("Choose a folder inside a mounted storage location.");
        }
    }

    private PixivDownloaderHelper Helper() => new(
        _services.GetRequiredService<IBOptionsManager<PixivOptions>>(),
        _services.GetRequiredService<IDownloaderLocalizer>(), new HttpClient(), _policy);

    private DownloadTaskService Tasks() => new(_services,
        new BakabaseLocalizer(_services.GetRequiredService<IStringLocalizer<SharedResource>>()), new NoopBus());

    private sealed class NoopBus : IWorkflowEventBus
    {
        public Task PublishAsync<T>(string triggerKind, T payload, CancellationToken ct = default) => Task.CompletedTask;
    }

    private DownloadTask NewTask(string path) => new()
    {
        Key = "fixture", ThirdPartyId = ThirdPartyId.Pixiv, Type = 1,
        DownloadPath = path, Status = DownloadTaskStatus.Idle
    };

    private AcquisitionStepContext Context() => new(
        _services, NullLogger.Instance, (_, _) => Task.CompletedTask, _internal, null);

    private static AcquisitionWorkItem Item() => new()
    {
        ResourceId = 1, LeadKind = AcquisitionLeadKind.Manual, LeadValue = "fixture", WorkingName = "output"
    };

    [TestMethod]
    public async Task DownloaderOptionsAndTaskCreationRejectOutsidePathsWithoutChangingSavedOptions()
    {
        var helper = Helper();
        await helper.PutOptionsAsync(new DownloaderOptions {DefaultPath = _mounted});
        await Assert.ThrowsExactlyAsync<IOException>(() =>
            helper.PutOptionsAsync(new DownloaderOptions {DefaultPath = _internal}));
        Assert.AreEqual(_mounted, (await helper.GetOptionsAsync()).DefaultPath);
        await Assert.ThrowsExactlyAsync<IOException>(() => helper.BuildTasks(new DownloadTaskAddInputModel
        {
            DownloadPath = _internal, Type = 1, Keys = ["tag"], ThirdPartyId = ThirdPartyId.Pixiv
        }));
        var allowed = await helper.BuildTasks(new DownloadTaskAddInputModel
        {
            DownloadPath = _mounted, Type = 1, Keys = ["tag"], ThirdPartyId = ThirdPartyId.Pixiv
        });
        Assert.AreEqual(_mounted, allowed.Single().DownloadPath);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    public async Task UnsetDownloadDirectoryIsNotInferredFromAvailableStorage(string? configured)
    {
        _services.GetRequiredService<IBOptionsManager<PixivOptions>>().Value.DefaultPath = configured;
        var helper = Helper();
        Assert.AreEqual(configured, (await helper.GetOptionsAsync()).DefaultPath);
        var input = new DownloadTaskAddInputModel
        {
            DownloadPath = configured!, Type = 1, Keys = ["tag"], ThirdPartyId = ThirdPartyId.Pixiv
        };
        await Assert.ThrowsExactlyAsync<IOException>(() => helper.BuildTasks(input));
        Assert.AreEqual(configured, input.DownloadPath);
    }

    [TestMethod]
    public async Task DirectTaskInsertionValidatesTheWholeBatchBeforeSaving()
    {
        var service = Tasks();
        await Assert.ThrowsExactlyAsync<IOException>(() => service.AddRange([NewTask(_mounted), NewTask(_internal)]));
        Assert.AreEqual(0, await _services.GetRequiredService<BakabaseDbContext>().DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task RestoredTaskWithUnavailableMountFailsVisiblyAndKeepsItsPath()
    {
        var service = Tasks();
        var task = (await service.AddRange([NewTask(_mounted)])).Data!.Single();
        _policy.DenyAll = true;
        var result = await service.TryStartAllTasks(DownloadTaskStartMode.ManualStart, [task.Id], DownloadTaskActionOnConflict.Ignore);
        Assert.AreNotEqual(0, result.Code);
        var stored = await _services.GetRequiredService<BakabaseDbContext>().DownloadTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(_mounted, stored.DownloadPath);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, stored.Status);
        StringAssert.Contains(stored.Message, "mounted storage");
    }

    [TestMethod]
    public async Task AcquisitionSetupValidatesAllPathsBeforeCreatingOrSavingAnything()
    {
        var inbox = Path.Combine(_mounted, "new-inbox");
        var outside = Path.Combine(_internal, "new-library");
        var options = _services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
        var previous = options.InboxDirectory;
        await Assert.ThrowsExactlyAsync<IOException>(() =>
            _services.GetRequiredService<AcquisitionSetupService>().ApplyAsync(new AcquisitionSetupInputModel
            {
                InboxDirectory = inbox, LibraryRootDirectory = outside
            }));
        Assert.IsFalse(Directory.Exists(inbox));
        Assert.IsFalse(Directory.Exists(outside));
        Assert.AreEqual(previous, options.InboxDirectory);
    }

    [TestMethod]
    public async Task PickingLocalDirectoryRechecksBothNewAnswersAndRestoredAnswers()
    {
        var step = new PickLocalDirectoryStep();
        await Assert.ThrowsExactlyAsync<IOException>(() => step.ResumeAsync(Context(), Item(),
            new AcquisitionResumeSignal(AcquisitionWaitReason.PickDirectory,
                JsonSerializer.Serialize(new PickLocalDirectoryStep.DirectorySignal(_internal))), default));
        await Assert.ThrowsExactlyAsync<IOException>(() => step.ExecuteAsync(Context(),
            Item() with {ExtractedDirectory = _internal}, default));
        var allowed = await step.ResumeAsync(Context(), Item(),
            new AcquisitionResumeSignal(AcquisitionWaitReason.PickDirectory,
                JsonSerializer.Serialize(new PickLocalDirectoryStep.DirectorySignal(_mounted))), default);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(allowed);
    }

    [TestMethod]
    public async Task PlacementAllowsInternalStagingButRejectsUserDestinationBeforeMoving()
    {
        var source = Path.Combine(_internal, "source.txt");
        await File.WriteAllTextAsync(source, "original");
        _services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.LibraryRootDirectory =
            Path.Combine(_root, "outside");
        await Assert.ThrowsExactlyAsync<IOException>(() => new PlaceStep().ExecuteAsync(Context(), Item(), default));
        Assert.AreEqual("original", await File.ReadAllTextAsync(source));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "outside")));
        AcquisitionStoragePaths.EnsureSourceAllowed(Context(), Item(), source);
        await Assert.ThrowsExactlyAsync<IOException>(() => Task.Run(() =>
            AcquisitionStoragePaths.EnsureSourceAllowed(Context(), Item(), Path.Combine(_root, "outside", "file"))));
    }

    [TestMethod]
    public async Task InternalExHentaiDownloadRequiresDatabaseOwnershipAndItsExactGeneratedDirectory()
    {
        const int acquisitionId = 73;
        var app = _services.GetRequiredService<AppService>();
        var directory = Path.Combine(Path.GetFullPath(app.AppDataDirectory), "acquisition", acquisitionId.ToString(), "platform");
        var helper = new ExHentaiDownloaderHelper(_services.GetRequiredService<IBOptionsManager<ExHentaiOptions>>(),
            _services.GetRequiredService<IDownloaderLocalizer>(), new HttpClient(), _policy, app);
        var input = new DownloadTaskAddInputModel
        {
            ThirdPartyId = ThirdPartyId.ExHentai, Type = (int) ExHentaiDownloadTaskType.SingleWork,
            Keys = ["https://exhentai.org/g/1/token/"], DownloadPath = directory
        };
        await Assert.ThrowsExactlyAsync<IOException>(() => helper.BuildTasks(input));
        var task = (await helper.BuildAcquisitionTasks(acquisitionId, input)).Single();
        var db = _services.GetRequiredService<BakabaseDbContext>();
        var row = task.ToDbModel()!;
        db.DownloadTasks.Add(row);
        await db.SaveChangesAsync();
        task.Id = row.Id;
        await Assert.ThrowsExactlyAsync<IOException>(() => DownloadTaskStorage.EnsureAllowedAsync(_services, task));
        db.DownloadResultOwners.Add(new DownloadResultOwnerDbModel
        {
            DownloadTaskId = row.Id, AcquisitionTaskId = acquisitionId, ResourceId = 1, WorkflowRunId = 1
        });
        await db.SaveChangesAsync();
        await DownloadTaskStorage.EnsureAllowedAsync(_services, task);
        task.DownloadPath = Path.Combine(app.AppDataDirectory, "bakabase.db");
        await Assert.ThrowsExactlyAsync<IOException>(() => DownloadTaskStorage.EnsureAllowedAsync(_services, task));
        task.DownloadPath = Path.Combine(Path.GetDirectoryName(directory)!, "different-task");
        await Assert.ThrowsExactlyAsync<IOException>(() => DownloadTaskStorage.EnsureAllowedAsync(_services, task));
    }

    [TestMethod]
    public async Task PlacementDoesNotCollapseIntoAnExternalLinkOrMoveItsContents()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Creating directory links may require elevated privileges on Windows.");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        var sourceFile = Path.Combine(outside, "original.txt");
        await File.WriteAllTextAsync(sourceFile, "untouched");
        Directory.CreateSymbolicLink(Path.Combine(_internal, "only-child"), outside);
        _services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.LibraryRootDirectory = _mounted;
        var result = await new PlaceStep().ExecuteAsync(Context(), Item(), default);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(result);
        Assert.AreEqual("untouched", await File.ReadAllTextAsync(sourceFile));
        Assert.IsFalse(Directory.Exists(Path.Combine(_mounted, "output")));
    }

    [TestMethod]
    public void InternalStagingExceptionDoesNotFollowLinksOutsideTheWorkDirectory()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Creating directory links may require elevated privileges on Windows.");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(_internal, "link");
        Directory.CreateSymbolicLink(link, outside);
        Assert.ThrowsExactly<IOException>(() =>
            AcquisitionStoragePaths.EnsureSourceAllowed(Context(), Item(), Path.Combine(link, "output")));
    }
}
