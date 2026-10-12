using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Patreon;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Fanbox;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Cien;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Fantia;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>Runs the real source dispatchers while their leaf handlers only record the target.</summary>
[TestClass]
public sealed class DownloaderDispatchDirectoryTests
{
    private interface IDispatchProbe
    {
        Task RunAsync(DownloadTask task);
    }

    private sealed class RecordingStorage : IUserStoragePolicy
    {
        public List<string> Checked { get; } = [];
        public bool IsRestricted => false;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => Checked.Add(path);
        public void EnsureTreeMutationAllowed(string path) => EnsurePathAllowed(path);
    }

    private sealed class PatreonProbe(IServiceProvider services, Action<string> capture)
        : AbstractPatreonDownloader(services, null!), IDispatchProbe
    {
        public override PatreonDownloadTaskType EnumTaskType => PatreonDownloadTaskType.SinglePost;
        public Task RunAsync(DownloadTask task) => StartCore(task, CancellationToken.None);
        private Task Record(string path)
        {
            capture(path);
            return Task.CompletedTask;
        }
        protected override Task DownloadFromCreator(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
        protected override Task DownloadFromPledging(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
        protected override Task DownloadSinglePost(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
        protected override Task DownloadByTier(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
        protected override Task DownloadFromCampaignFeed(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
    }

    private sealed class FanboxProbe(IServiceProvider services, Action<string> capture)
        : AbstractFanboxDownloader(services, null!), IDispatchProbe
    {
        public override FanboxDownloadTaskType EnumTaskType => FanboxDownloadTaskType.SinglePost;
        public Task RunAsync(DownloadTask task) => StartCore(task, CancellationToken.None);
        private Task Record(string path)
        {
            capture(path);
            return Task.CompletedTask;
        }
        protected override Task DownloadFromCreator(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
        protected override Task DownloadFromFollowing(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
        protected override Task DownloadSinglePost(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
    }

    private sealed class CienProbe(IServiceProvider services, Action<string> capture)
        : AbstractCienDownloader(services, null!), IDispatchProbe
    {
        public override CienDownloadTaskType EnumTaskType => CienDownloadTaskType.SinglePost;
        public Task RunAsync(DownloadTask task) => StartCore(task, CancellationToken.None);
        private Task Record(string path)
        {
            capture(path);
            return Task.CompletedTask;
        }
        protected override Task DownloadFromAuthor(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
        protected override Task DownloadFromFollowing(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
        protected override Task DownloadSingleArticle(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
        protected override Task DownloadByCategory(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
    }

    private sealed class FantiaProbe(IServiceProvider services, Action<string> capture)
        : AbstractFantiaDownloader(services, null!), IDispatchProbe
    {
        public override FantiaDownloadTaskType EnumTaskType => FantiaDownloadTaskType.SinglePost;
        public Task RunAsync(DownloadTask task) => StartCore(task, CancellationToken.None);
        private Task Record(string path)
        {
            capture(path);
            return Task.CompletedTask;
        }
        protected override Task DownloadFromFanclub(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
        protected override Task DownloadFromFollowing(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
        protected override Task DownloadSinglePost(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
        protected override Task DownloadByPlan(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct) => Record(downloadPath);
    }

    [ClassInitialize]
    public static void Register(TestContext _)
    {
        DownloaderInternals.DownloaderTypeDefinitionMap.TryAdd(typeof(PatreonProbe), new DownloaderDefinition
        {
            ThirdPartyId = ThirdPartyId.Patreon, TaskType = 3,
            EnumTaskType = PatreonDownloadTaskType.SinglePost, DownloaderType = typeof(PatreonProbe),
            DefaultConvention = "fixture", Name = "Dispatch probe"
        });
        DownloaderInternals.DownloaderTypeDefinitionMap.TryAdd(typeof(FanboxProbe), new DownloaderDefinition
        {
            ThirdPartyId = ThirdPartyId.Fanbox, TaskType = 3,
            EnumTaskType = FanboxDownloadTaskType.SinglePost, DownloaderType = typeof(FanboxProbe),
            DefaultConvention = "fixture", Name = "Dispatch probe"
        });
        DownloaderInternals.DownloaderTypeDefinitionMap.TryAdd(typeof(CienProbe), new DownloaderDefinition
        {
            ThirdPartyId = ThirdPartyId.Cien, TaskType = 3,
            EnumTaskType = CienDownloadTaskType.SinglePost, DownloaderType = typeof(CienProbe),
            DefaultConvention = "fixture", Name = "Dispatch probe"
        });
        DownloaderInternals.DownloaderTypeDefinitionMap.TryAdd(typeof(FantiaProbe), new DownloaderDefinition
        {
            ThirdPartyId = ThirdPartyId.Fantia, TaskType = 3,
            EnumTaskType = FantiaDownloadTaskType.SinglePost, DownloaderType = typeof(FantiaProbe),
            DefaultConvention = "fixture", Name = "Dispatch probe"
        });
    }

    [TestMethod]
    [DataRow(ThirdPartyId.Patreon, "task")]
    [DataRow(ThirdPartyId.Fanbox, "task")]
    [DataRow(ThirdPartyId.Cien, "task")]
    [DataRow(ThirdPartyId.Fantia, "task")]
    [DataRow(ThirdPartyId.Patreon, "source")]
    [DataRow(ThirdPartyId.Fanbox, "source")]
    [DataRow(ThirdPartyId.Cien, "source")]
    [DataRow(ThirdPartyId.Fantia, "source")]
    [DataRow(ThirdPartyId.Patreon, "global")]
    [DataRow(ThirdPartyId.Fanbox, "global")]
    [DataRow(ThirdPartyId.Cien, "global")]
    [DataRow(ThirdPartyId.Fantia, "global")]
    public async Task DispatchUsesCapturedTaskDirectoryEvenAfterDefaultsChange(ThirdPartyId source, string selected)
    {
        var storage = new RecordingStorage();
        var services = await TestServiceBuilder.BuildServiceProvider(s => s.AddSingleton<IUserStoragePolicy>(storage));
        try
        {
            ISimpleDownloaderOptionsHolder options = source switch
            {
                ThirdPartyId.Patreon => services.GetRequiredService<IBOptions<PatreonOptions>>().Value,
                ThirdPartyId.Fanbox => services.GetRequiredService<IBOptions<FanboxOptions>>().Value,
                ThirdPartyId.Cien => services.GetRequiredService<IBOptions<CienOptions>>().Value,
                ThirdPartyId.Fantia => services.GetRequiredService<IBOptions<FantiaOptions>>().Value,
                _ => throw new ArgumentOutOfRangeException(nameof(source))
            };
            var global = services.GetRequiredService<IBOptions<DownloaderGlobalOptions>>().Value;
            var root = Path.Combine(Path.GetTempPath(), "bakabase-dispatch-fixture");
            global.DefaultDownloadPath = Path.Combine(root, "global");
            options.DefaultPath = selected == "global" ? null : Path.Combine(root, "source");
            var explicitPath = selected == "task" ? Path.Combine(root, "task") : null;
            var expected = selected switch
            {
                "task" => explicitPath!,
                "source" => options.DefaultPath!,
                _ => Path.Combine(root, "global", source.ToString())
            };
            var helper = services.GetRequiredService<IDownloaderFactory>().GetHelper(source, 3);
            var tasks = await helper.BuildTasks(new DownloadTaskAddInputModel
            {
                ThirdPartyId = source, Type = 3, Keys = ["1"], DownloadPath = explicitPath
            });
            Assert.AreEqual(1, tasks.Length);
            Assert.AreEqual(expected, tasks[0].DownloadPath);

            // Changing configuration affects future tasks; an already-created task must keep
            // the directory shown in its details, whether it was explicit or inherited.
            global.DefaultDownloadPath = Path.Combine(root, "new-global");
            options.DefaultPath = Path.Combine(root, "new-source");
            storage.Checked.Clear();
            var dispatched = new List<string>();
            IDispatchProbe probe = source switch
            {
                ThirdPartyId.Patreon => new PatreonProbe(services, dispatched.Add),
                ThirdPartyId.Fanbox => new FanboxProbe(services, dispatched.Add),
                ThirdPartyId.Cien => new CienProbe(services, dispatched.Add),
                ThirdPartyId.Fantia => new FantiaProbe(services, dispatched.Add),
                _ => throw new ArgumentOutOfRangeException(nameof(source))
            };
            await probe.RunAsync(tasks[0]);
            CollectionAssert.AreEqual(new[] {expected}, dispatched);
            CollectionAssert.AreEqual(new[] {expected}, storage.Checked,
                "The source dispatcher must validate the exact directory passed to the download handler.");
        }
        finally
        {
            (services as IDisposable)?.Dispose();
        }
    }
}
