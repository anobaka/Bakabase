using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Bilibili;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Cien;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Fanbox;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Fantia;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Patreon;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Pixiv;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Service.Controllers;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloaderDefaultDirectoryTests
{
    private sealed class Options<T>(T value) : IBOptionsManager<T> where T : class
    {
        public T Value { get; } = value;
        public int Saves { get; private set; }
        public void Save(T options) => throw new NotSupportedException();
        public Task SaveAsync(T options) => throw new NotSupportedException();
        public Task SaveAsync(Action<T> modify)
        {
            modify(Value);
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class MountedStorage : IUserStoragePolicy
    {
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            [new("/nas-bakabase", "NAS", "volume", false)];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            path == "/nas-bakabase" || path?.StartsWith("/nas-bakabase/", StringComparison.Ordinal) == true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            if (!IsPathAllowed(path, purpose)) throw new UserStoragePathException("Outside mounted storage.");
        }
        public void EnsureTreeMutationAllowed(string path) => EnsurePathAllowed(path);
    }

    // Bilibili validates a favorites id when creating a task. These two responses keep that
    // existing validation real without contacting the user's account or the network.
    private sealed class FavoritesHttpFactory : IHttpClientFactory
    {
        private sealed class Handler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("myinfo")
                        ? "{\"code\":0,\"data\":{\"profile\":{\"mid\":1}}}"
                        : "{\"code\":0,\"data\":{\"list\":[{\"id\":1,\"title\":\"Favorites\"}]}}")
                });
        }
        public HttpClient CreateClient(string name) => new(new Handler());
    }

    private static (IDownloaderHelper Helper, ISimpleDownloaderOptionsHolder Source, DownloaderGlobalOptions Global)
        Create(ThirdPartyId source, IUserStoragePolicy? storage = null)
    {
        var global = new DownloaderGlobalOptions {DefaultDownloadPath = "/nas-bakabase/Downloads"};
        var globals = new Options<DownloaderGlobalOptions>(global);
        var policy = storage ?? new UserStoragePolicy(isContainer: false);
        var http = new HttpClient();

        (IDownloaderHelper, ISimpleDownloaderOptionsHolder) Wrap<T>(T options,
            Func<Options<T>, IDownloaderHelper> build) where T : class, ISimpleDownloaderOptionsHolder =>
            (build(new Options<T>(options)), options);

        var (helper, options) = source switch
        {
            ThirdPartyId.ExHentai => Wrap(new ExHentaiOptions(), o => new ExHentaiDownloaderHelper(o,
                null!, http, policy, null!, globals)),
            ThirdPartyId.Pixiv => Wrap(new PixivOptions(), o => new PixivDownloaderHelper(o,
                null!, http, policy, globals)),
            ThirdPartyId.Bilibili => Wrap(new BilibiliOptions(), o => new BilibiliDownloaderHelper(o,
                null!, new BilibiliClient(null!, new FavoritesHttpFactory(), NullLoggerFactory.Instance),
                http, null!, policy, globals)),
            ThirdPartyId.Fanbox => Wrap(new FanboxOptions(), o => new FanboxDownloaderHelper(o,
                null!, http, policy, globals)),
            ThirdPartyId.Fantia => Wrap(new FantiaOptions(), o => new FantiaDownloaderHelper(o,
                null!, http, policy, globals)),
            ThirdPartyId.Cien => Wrap(new CienOptions(), o => new CienDownloaderHelper(o,
                null!, http, policy, globals)),
            ThirdPartyId.Patreon => Wrap(new PatreonOptions(), o => new PatreonDownloaderHelper(o,
                null!, http, policy, globals)),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        return (helper, options, global);
    }

    private static DownloadTaskAddInputModel Input(ThirdPartyId source, string? path = null, int type = 1) => new()
    {
        ThirdPartyId = source, Type = type, Keys = ["1"], DownloadPath = path
    };

    [TestMethod]
    [DataRow(ThirdPartyId.ExHentai)]
    [DataRow(ThirdPartyId.Pixiv)]
    [DataRow(ThirdPartyId.Bilibili)]
    [DataRow(ThirdPartyId.Fanbox)]
    [DataRow(ThirdPartyId.Fantia)]
    [DataRow(ThirdPartyId.Cien)]
    [DataRow(ThirdPartyId.Patreon)]
    public async Task AllSourceHelpersUseTaskThenSourceThenGlobalDirectory(ThirdPartyId source)
    {
        var (helper, options, global) = Create(source);
        var inherited = (await helper.BuildTasks(Input(source))).Single();
        Assert.AreEqual(Path.Combine(global.DefaultDownloadPath!, source.ToString()), inherited.DownloadPath);
        Assert.IsNull(options.DefaultPath);

        options.DefaultPath = "/source-configured";
        Assert.AreEqual("/source-configured", (await helper.BuildTasks(Input(source))).Single().DownloadPath);
        Assert.AreEqual("/task-override", (await helper.BuildTasks(Input(source, "/task-override"))).Single().DownloadPath);
        global.DefaultDownloadPath = "/changed-global-root";
        Assert.AreEqual(Path.Combine("/nas-bakabase/Downloads", source.ToString()), inherited.DownloadPath,
            "Existing tasks must keep the directory captured at creation.");
    }

    [TestMethod]
    public async Task SourceOptionsExposeInheritanceWithoutPersistingItOnUnrelatedEdits()
    {
        var (helper, source, global) = Create(ThirdPartyId.ExHentai);
        var response = await helper.GetOptionsAsync();
        Assert.IsNull(response.DefaultPath);
        Assert.AreEqual(Path.Combine("/nas-bakabase/Downloads", "ExHentai"), response.EffectiveDefaultPath);
        response.NamingConvention = "edited";
        await helper.PutOptionsAsync(response);
        Assert.IsNull(source.DefaultPath);
        Assert.AreEqual("edited", source.NamingConvention);
        global.DefaultDownloadPath = "/new-root";
        Assert.AreEqual(Path.Combine("/new-root", "ExHentai"), (await helper.GetOptionsAsync()).EffectiveDefaultPath);

        source.DefaultPath = "/source-dir";
        Assert.AreEqual("/source-dir", (await helper.GetOptionsAsync()).EffectiveDefaultPath);
        Assert.AreEqual("/source-dir", (await helper.GetOptionsAsync()).DefaultPath);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public async Task NoConfiguredDefaultStillRequiresAUserDirectory(string? unset)
    {
        var (helper, source, global) = Create(ThirdPartyId.ExHentai);
        source.DefaultPath = unset;
        global.DefaultDownloadPath = unset;
        Assert.IsNull((await helper.GetOptionsAsync()).EffectiveDefaultPath);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => helper.BuildTasks(Input(ThirdPartyId.ExHentai)));
    }

    [TestMethod]
    public async Task SourcesShareTheSameDirectoryAcrossTaskTypes()
    {
        var (helper, _, _) = Create(ThirdPartyId.ExHentai);
        var single = (await helper.BuildTasks(Input(ThirdPartyId.ExHentai, type: 1))).Single();
        var watched = (await helper.BuildTasks(Input(ThirdPartyId.ExHentai, type: 2))).Single();
        var list = (await helper.BuildTasks(Input(ThirdPartyId.ExHentai, type: 3))).Single();
        Assert.AreEqual(single.DownloadPath, watched.DownloadPath);
        Assert.AreEqual(single.DownloadPath, list.DownloadPath);
    }

    [TestMethod]
    public void GlobalRootUsesNativeDirectorySeparatorsIncludingTrailingSeparatorAndFilesystemRoot()
    {
        var root = OperatingSystem.IsWindows() ? @"D:\Downloads\" : "/nas-bakabase/Downloads/";
        var expected = OperatingSystem.IsWindows() ? @"D:\Downloads\ExHentai" : "/nas-bakabase/Downloads/ExHentai";
        Assert.AreEqual(expected, DownloadDirectoryResolver.Resolve(null, null, root, ThirdPartyId.ExHentai));
        var filesystemRoot = OperatingSystem.IsWindows() ? @"D:\" : "/";
        var expectedAtRoot = OperatingSystem.IsWindows() ? @"D:\Pixiv" : "/Pixiv";
        Assert.AreEqual(expectedAtRoot, DownloadDirectoryResolver.Resolve(null, " ", filesystemRoot, ThirdPartyId.Pixiv));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DownloadDirectoryResolver.Resolve(null, null, root, (ThirdPartyId)999));
    }

    [TestMethod]
    public async Task InheritedDirectoryIsValidatedBeforeCreatingContainerTasks()
    {
        var (helper, source, global) = Create(ThirdPartyId.ExHentai, new MountedStorage());
        global.DefaultDownloadPath = "/tmp";
        await Assert.ThrowsExactlyAsync<UserStoragePathException>(() => helper.BuildTasks(Input(ThirdPartyId.ExHentai)));
        source.DefaultPath = "/nas-bakabase/Configured";
        Assert.AreEqual("/nas-bakabase/Configured", (await helper.BuildTasks(Input(ThirdPartyId.ExHentai))).Single().DownloadPath);
        await Assert.ThrowsExactlyAsync<UserStoragePathException>(() =>
            helper.BuildTasks(Input(ThirdPartyId.ExHentai, "/tmp/explicit")));
    }

    [TestMethod]
    public async Task GlobalPatchPreservesOmittedDirectoryAndClearsWithEmptyString()
    {
        var options = new Options<DownloaderGlobalOptions>(new()
        {
            DefaultDownloadPath = "/nas-bakabase/Downloads", AutoStartAfterCreation = false
        });
        using var services = new ServiceCollection().BuildServiceProvider();
        var pool = new BakabaseOptionsManagerPool(services);
        pool.AllOptionsManagers[typeof(DownloaderGlobalOptions)] = options;
        var controller = new OptionsController(null!, null!, pool, null!, null!, null!, null!, new MountedStorage());

        await controller.PatchDownloaderGlobalOptions(new() {AutoStartAfterCreation = true});
        Assert.AreEqual("/nas-bakabase/Downloads", options.Value.DefaultDownloadPath);
        await Assert.ThrowsExactlyAsync<UserStoragePathException>(() => controller.PatchDownloaderGlobalOptions(new()
        {
            DefaultDownloadPath = "/tmp", AutoStartAfterCreation = false
        }));
        Assert.AreEqual(1, options.Saves);
        Assert.IsTrue(options.Value.AutoStartAfterCreation);

        await controller.PatchDownloaderGlobalOptions(new() {DefaultDownloadPath = "/nas-bakabase/Other"});
        Assert.AreEqual("/nas-bakabase/Other", options.Value.DefaultDownloadPath);
        await controller.PatchDownloaderGlobalOptions(new() {DefaultDownloadPath = ""});
        Assert.IsNull(options.Value.DefaultDownloadPath);
        Assert.IsTrue(options.Value.AutoStartAfterCreation);
    }
}
