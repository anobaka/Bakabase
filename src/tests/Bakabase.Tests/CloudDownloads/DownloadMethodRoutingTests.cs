using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Models;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.SourceAvailability;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class DownloadMethodRoutingTests
{
    [TestMethod]
    public async Task Unavailable_integration_is_not_selectable_even_with_installed_tool_and_connected_account()
    {
        var account = new ConnectedAccount();
        var provider = new Provider("terabox.cli", "terabox");
        var registry = new DownloadMethodRegistry([provider],
            [new InstalledDependency("terabox-cli-component-service", allowInspection: false)], null!, null!,
            accountResolvers: [account]);
        var availability = new SourceAvailabilityService(null!, [new AccountScopedProbe()], accountResolvers: [account]);
        var service = new DownloadIntentService(registry, null!, new UserStoragePolicy(false), null!, null!, null!,
            null!, null!, null!, availability: availability);
        var view = await service.PreflightAsync(new()
        {
            Candidates = [new() {Id = "item", Url = "https://terabox.com/s/1ExampleId"}],
            SelectedCandidateIds = ["item"], CheckSourceAvailability = true
        }, Path.GetTempPath(), default);
        Assert.IsFalse(view.CanSubmit);
        var candidate = view.Candidates.Single();
        Assert.IsFalse(candidate.CanSelect);
        Assert.IsTrue(candidate.Methods.Single().Supported);
        Assert.IsFalse(candidate.Methods.Single().Ready);
        Assert.AreEqual("unavailable", candidate.State);
        Assert.AreEqual(DownloadMethodAvailabilityConstraint.ReasonCode, candidate.Requirements.Single().Code);
        Assert.IsNull(candidate.Availability, "A capability limitation must not invent a link-validity observation.");
        Assert.AreEqual(0, account.ResolveCalls);
        Assert.AreEqual(0, provider.ReadyCalls);
    }

    [TestMethod]
    public async Task Legacy_download_starter_rejects_unavailable_method_before_binding_account_or_creating_workflow()
    {
        var account = new ConnectedAccount();
        var registry = new DownloadMethodRegistry([], [], null!, null!, accountResolvers: [account]);
        using var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
        var starter = new DownloadIntentStarter(null!, null!, new UserStoragePolicy(false), services);
        var source = DownloadSourceResolver.Normalize(new() {Id = "s", Url = "https://terabox.com/s/1ExampleId"});
        var item = CloudWorkflow.FromSources([source], null, "origin", Path.GetTempPath());
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            starter.StartFilesAsync(item, null, null, default));
        StringAssert.Contains(error.Message, "temporarily unavailable");
        Assert.AreEqual(0, account.ResolveCalls);
    }

    [TestMethod]
    public async Task Direct_download_creation_cannot_bypass_unavailable_integration_preflight()
    {
        var provider = new Provider("terabox.cli", "terabox");
        var helper = new CloudDownloaderHelper(new UserStoragePolicy(false), new DefaultGlobalOptions(), [provider]);
        var source = DownloadSourceResolver.Normalize(new() {Id = "s", Url = "https://terabox.com/s/1ExampleId"});
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => helper.BuildTasks(new()
        {
            ThirdPartyId = ThirdPartyId.TeraBox, Type = 1, Keys = [source.Url], DownloadPath = Path.GetTempPath(),
            Options = JsonSerializer.Serialize(new CloudDownloadRequest("new-operation", source, "connected-account", null,
                Path.GetTempPath()), JsonSerializerOptions.Web)
        }));
        StringAssert.Contains(error.Message, "temporarily unavailable");
        Assert.AreEqual(0, provider.ReadyCalls);
    }

    [TestMethod]
    [DataRow("https://pixeldrain.com/d/abcd1234", "pixeldrain.rclone")]
    [DataRow("https://pixeldrain.com/u/abcd1234", "pixeldrain.galleryDl")]
    [DataRow("https://pixeldrain.com/l/abcd1234", "pixeldrain.galleryDl")]
    public async Task Preflight_selected_method_is_the_actual_execution_engine(string url, string expected)
    {
        var legacy = new Provider(null);
        var rclone = new Provider("pixeldrain.rclone");
        var registry = Registry(legacy, rclone);
        var view = await Service(registry).PreflightAsync(new()
        {
            Candidates = [new() {Id = "item", Url = url}], SelectedCandidateIds = ["item"]
        }, Path.GetTempPath(), default);
        Assert.IsTrue(view.CanSubmit);
        var candidate = view.Candidates.Single();
        Assert.AreEqual(expected, candidate.SelectedMethodId);
        Assert.AreEqual(2, candidate.Methods.Count);
        var source = candidate.ResolvedSource!;
        Assert.AreSame(expected == "pixeldrain.rclone" ? rclone : legacy,
            DownloadProviderRouting.Require([legacy, rclone], source));
    }

    [TestMethod]
    public async Task An_unselected_ready_public_method_cannot_hide_an_unsupported_selected_method()
    {
        var view = await Service(Registry(new Provider(null), new Provider("pixeldrain.rclone"))).PreflightAsync(new()
        {
            Candidates = [new() {Id = "item", Url = "https://pixeldrain.com/u/abcd1234", MethodId = "pixeldrain.rclone"}],
            SelectedCandidateIds = ["item"]
        }, Path.GetTempPath(), default);
        Assert.IsFalse(view.CanSubmit);
        var candidate = view.Candidates.Single();
        Assert.IsTrue(candidate.Methods.Single(m => m.Id == "pixeldrain.galleryDl").Ready);
        Assert.AreEqual("pixeldrain.rclone", candidate.SelectedMethodId);
        Assert.IsTrue(candidate.Requirements.Any(r => r.Code == "PIXELDRAIN_DIRECTORY_REQUIRED"));
    }

    [TestMethod]
    public async Task Missing_rclone_dependency_blocks_only_the_selected_rclone_method()
    {
        var registry = new DownloadMethodRegistry([new Provider(null), new Provider("pixeldrain.rclone")],
            [new InstalledDependency("gallery-dl-component-service")], null!, null!);
        var view = await Service(registry).PreflightAsync(new()
        {
            Candidates = [new() {Id = "item", Url = "https://pixeldrain.com/u/abcd1234"}], SelectedCandidateIds = ["item"]
        }, Path.GetTempPath(), default);
        Assert.IsTrue(view.CanSubmit);
        Assert.IsTrue(view.Candidates.Single().Methods.Single(m => m.Id == "pixeldrain.rclone").Requirements
            .Any(r => r.Code == "DEPENDENCY_UNAVAILABLE"));
    }

    [TestMethod]
    [DataRow("pikpak", "pikpak.rclone")]
    [DataRow("oneDrive", "oneDrive.rclone")]
    public void Account_paths_are_distinct_from_share_links_and_preserve_method_and_account(string provider, string method)
    {
        var id = Guid.NewGuid().ToString("N");
        var source = DownloadSourceResolver.Normalize(new() {Id = "remote", Kind = "remote", Url = "/folder/file.zip",
            ProviderId = provider, MethodId = method, AccountId = id});
        Assert.AreEqual(method, source.MethodId); Assert.AreEqual(id, source.AccountId);
        var share = provider == "pikpak" ? "https://mypikpak.com/s/example" : "https://tenant.sharepoint.com/:f:/g/example";
        Assert.ThrowsExactly<ArgumentException>(() => DownloadSourceResolver.Normalize(new()
            {Id = "share", Url = share, ProviderId = provider, MethodId = method}));
    }

    [TestMethod]
    public void Reconfigured_account_identity_cannot_reuse_previous_download_artifacts()
    {
        CloudSource source = new() {Id = "source", Kind = "remote", Url = "/folder", ProviderId = "pikpak",
            MethodId = "pikpak.rclone", AccountId = Guid.NewGuid().ToString("N"), AccountVersion = "generation-one"};
        Assert.AreNotEqual(CloudSourceIdentity.Key(source), CloudSourceIdentity.Key(source with {AccountVersion = "generation-two"}));
        Assert.AreEqual(CloudSourceIdentity.Key(source), CloudSourceIdentity.Key(source with {Id = "other-ui-origin"}));
        var set = CloudWorkflow.FromSources([source], null, "origin", Path.GetTempPath());
        set = set with {Targets = [set.Targets.Single() with {SelectedSourceId = source.Id, DownloadTaskId = 42}]};
        Assert.ThrowsExactly<InvalidOperationException>(() => DownloadWorksetRouting.ApplySelections(set, new()
            {SourceAccounts = {{source.Id, Guid.NewGuid().ToString("N")}}}));
    }

    [TestMethod]
    public async Task A_new_public_engine_does_not_reuse_legacy_requests_but_default_method_checkpoints_remain_compatible()
    {
        CloudSource legacyDirectory = new() {Id = "old-directory", ProviderId = "pixeldrain", Url = "https://pixeldrain.com/d/abcd1234"};
        var resolved = await new DownloadSourceResolver([]).ResolveAsync(legacyDirectory, default);
        Assert.AreEqual("pixeldrain.rclone", resolved.MethodId);
        Assert.AreNotEqual(CloudSourceIdentity.Key(legacyDirectory), CloudSourceIdentity.Key(resolved));
        var reloaded = await new DownloadSourceResolver([]).ResolveAsync(resolved, default);
        Assert.AreEqual(CloudSourceIdentity.Key(resolved), CloudSourceIdentity.Key(reloaded));
        CloudSource baidu = new() {Id = "old-share", ProviderId = "baidu", Url = "https://pan.baidu.com/s/example"};
        Assert.AreEqual(CloudSourceIdentity.Key(baidu), CloudSourceIdentity.Key(baidu with {MethodId = "baidu.pcs"}));
    }

    private static DownloadMethodRegistry Registry(params ICloudDownloadProvider[] providers) => new(providers,
        [new InstalledDependency("gallery-dl-component-service"), new InstalledDependency("rclone-component-service")], null!, null!);
    private static DownloadIntentService Service(DownloadMethodRegistry registry) => new(registry, null!,
        new UserStoragePolicy(false), null!, null!, null!, null!, null!, null!);

    private sealed class Provider(string? method, string providerId = "pixeldrain") : ICloudDownloadProvider
    {
        public string ProviderId => providerId;
        public string? MethodId => method;
        public int ReadyCalls { get; private set; }
        public Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct)
        {
            ReadyCalls++;
            Assert.IsNull(accountId); return Task.FromResult(new CloudReadiness(true));
        }
        public Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request, Func<CloudDownloadProgress, Task> progress, CancellationToken ct) =>
            throw new AssertFailedException("Preflight must not start a download.");
    }

    private sealed class ConnectedAccount : ICloudDownloadAccountResolver
    {
        public string AccountProvider => "terabox";
        public int ResolveCalls { get; private set; }
        public Task<CloudAccountReference> ResolveAccountAsync(string? accountId, string? accountVersion, CancellationToken ct)
        {
            ResolveCalls++;
            return Task.FromResult(new CloudAccountReference("connected-account", "current-generation"));
        }
    }

    private sealed class DefaultGlobalOptions : IBOptions<DownloaderGlobalOptions>
    {
        public DownloaderGlobalOptions Value { get; } = new();
    }

    private sealed class AccountScopedProbe : ISourceAvailabilityProbe
    {
        public bool UsesAccountScope => true;
        public bool CanHandle(CloudSource source) => source.ProviderId == "terabox";
        public Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct) =>
            throw new AssertFailedException("Automatic preflight must not probe a known unavailable integration.");
    }

    private sealed class InstalledDependency(string id, bool allowInspection = true) : IDependentComponentService
    {
        public string Id => id;
        public string DisplayName => id;
        public string? Description => null;
        public string DefaultLocation => "";
        public bool IsRequired => false;
        public bool IsAvailableOnCurrentPlatform
        {
            get { Assert.IsTrue(allowInspection, "Known integration limitations must not probe dependencies."); return true; }
        }
        public bool CanAutoInstallOnCurrentPlatform => true;
        public IReadOnlyList<DependentComponentTarget> SupportedTargets => [];
        public IReadOnlyList<string> DependencyIds => [];
        public DependentComponentStatus Status
        {
            get { Assert.IsTrue(allowInspection, "Known integration limitations must not probe dependencies."); return DependentComponentStatus.Installed; }
        }
        public DependentComponentContext Context => throw new AssertFailedException("No context read expected.");
        public event Func<DependentComponentContext, Task>? OnStateChange { add {} remove {} }
        public Task EnsureReadyAsync(CancellationToken ct) => throw new AssertFailedException("No dependency install expected.");
        public Task Install(CancellationToken ct) => throw new AssertFailedException("Preflight must not install.");
        public Task<DependentComponentVersion> GetLatestVersion(bool fromCache, CancellationToken ct) => throw new AssertFailedException("No update check expected.");
        public Task Discover(CancellationToken ct) => throw new AssertFailedException("Installed dependencies need no discovery.");
    }
}
