using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Abstractions.Components.FileSystem;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadIntentRoutingTests
{
    [DataTestMethod]
    [DataRow("https://example.test/post/123", "auto", "unknown", "manual")]
    [DataRow("https://example.test/post/123", "http", "http", "http.native")]
    [DataRow("https://files.example.test/archive.rar?token=preserved", "auto", "http", "http.native")]
    [DataRow("magnet:?xt=urn:btih:0123456789012345678901234567890123456789", "auto", "magnet", "bt.native")]
    [DataRow("https://example.test/item.torrent", "auto", "torrent", "bt.native")]
    [DataRow("https://example.test/metadata", "torrent", "torrent", "bt.native")]
    [DataRow("https://pan.baidu.com/s/1test", "auto", "baidu", "baidu.pcs")]
    [DataRow("https://drive.google.com/drive/folders/test", "auto", "googleDrive", "googleDrive.gdown")]
    [DataRow("https://gofile.io/d/test", "auto", "gofile", "gofile.galleryDl")]
    [DataRow("https://www.feemoo.com/s/test", "auto", "feemoo", "manual")]
    [DataRow("https://feemoo.com/s/test", "auto", "feemoo", "manual")]
    [DataRow("https://feemoo.com/account", "auto", "unknown", "manual")]
    [DataRow("https://share.weiyun.com/Ab30x7Zq?source=fixture", "auto", "weiyun", "manual")]
    [DataRow("https://share.weiyun.com/Ab30x7Zq/", "share", "weiyun", "manual")]
    [DataRow("https://share.weiyun.com/", "auto", "unknown", "manual")]
    [DataRow("https://share.weiyun.com/extra/path.zip", "auto", "unknown", "manual")]
    [DataRow("https://share.weiyun.com.example.test/Ab30x7Zq", "auto", "unknown", "manual")]
    [DataRow("https://files.example.test/page.zip", "share", "unknown", "manual")]
    [DataRow("https://mypikpak.com/s/test?pwd=code", "auto", "pikpak", "pikpak.share")]
    [DataRow("https://mypikpak.com/account", "auto", "unknown", "manual")]
    [DataRow("https://pixeldrain.com/u/test", "auto", "pixeldrain", "pixeldrain.galleryDl")]
    [DataRow("https://pixeldrain.com/l/test#item=1", "auto", "pixeldrain", "pixeldrain.galleryDl")]
    [DataRow("https://pixeldrain.com/account", "auto", "unknown", "manual")]
    [DataRow("https://tenant.sharepoint.com/:f:/g/personal/share?e=token", "auto", "oneDrive", "oneDrive.sharePoint")]
    [DataRow("https://e-hentai.org/g/123/abc/", "auto", "exHentai", "site.exHentai")]
    public void RoutingRequiresAnExplicitSupportedMethodInsteadOfGuessingWebpagesAreFiles(string url, string kind, string provider, string method)
    {
        var result = DownloadMethodRegistry.Normalize(new() {Id = "source", Url = url, Kind = kind});
        Assert.AreEqual(provider, result.ProviderId);
        Assert.AreEqual(method, DownloadMethodRegistry.MethodId(result.ProviderId));
        Assert.AreEqual(url, result.Url);
    }

    [DataTestMethod]
    [DataRow("https://pan.baidu.com/s/1test", "http")]
    [DataRow("https://drive.google.com/drive/folders/test", "http")]
    [DataRow("https://gofile.io/d/test", "torrent")]
    [DataRow("https://share.weiyun.com/Ab30x7Zq", "http")]
    [DataRow("https://share.weiyun.com/Ab30x7Zq", "torrent")]
    [DataRow("https://username:password@example.test/file.zip", "auto")]
    [DataRow("file:///etc/passwd", "auto")]
    [DataRow("https://example.test/file.zip", "unsupported-kind")]
    public void InvalidOrConflictingMethodsAreRejected(string url, string kind) =>
        Assert.ThrowsExactly<ArgumentException>(() => DownloadMethodRegistry.Normalize(new() {Id = "source", Url = url, Kind = kind}));

    [TestMethod]
    public void AClaimedMethodCannotOverrideTheResolvedSourceAdapter() =>
        Assert.ThrowsExactly<ArgumentException>(() => DownloadMethodRegistry.Normalize(new()
            {Id = "source", Url = "https://pan.baidu.com/s/1test", MethodId = "http.native"}));

    [TestMethod]
    public async Task LegacyWeiyunShareIsRecognizedWithoutSuggestingAnAccountOrPermittingDownload()
    {
        const string url = "https://share.weiyun.com/Ab30x7Zq?source=fixture";
        var input = new DownloadIntentRequest
        {
            Candidates = [new() {Id = "legacy", Url = url, Kind = "share", ProviderId = "unknown"}],
            SelectedCandidateIds = ["legacy"]
        };
        var preflight = await Service().PreflightDownloadsOnlyAsync(input, Path.GetTempPath(), default);
        Assert.IsFalse(preflight.CanSubmit);
        var candidate = preflight.Candidates.Single();
        Assert.IsFalse(candidate.CanSelect);
        Assert.AreEqual(url, candidate.Candidate.Url);
        Assert.AreEqual("weiyun", candidate.ResolvedSource!.ProviderId);
        Assert.AreEqual("manual", candidate.SelectedMethodId);
        Assert.IsNull(candidate.Methods.Single().AccountProvider);
        var requirement = candidate.Requirements.Single();
        Assert.AreEqual("WEIYUN_PUBLIC_SHARE_UNSUPPORTED", requirement.Code);
        CollectionAssert.AreEqual(new[] {"chooseSource"}, requirement.Actions!.ToArray());
    }

    [TestMethod]
    public async Task AccountSelectionDoesNotBlockNativeMethodsAndUnsupportedSourcesRemainVisible()
    {
        var registry = new DownloadMethodRegistry([], [], null!, null!);
        var native = await registry.DescribeAsync(DownloadMethodRegistry.Normalize(new()
            {Id = "http", Url = "https://example.test/file.zip"}), "irrelevant-account", default);
        Assert.IsTrue(native.Ready);
        Assert.IsNull(native.AccountProvider);
        var unsupported = await registry.DescribeAsync(DownloadMethodRegistry.Normalize(new()
            {Id = "page", Url = "https://example.test/post/123"}), null, default);
        Assert.IsFalse(unsupported.Supported);
        Assert.IsFalse(unsupported.Ready);
        Assert.AreEqual("SOURCE_UNSUPPORTED", unsupported.Requirements.Single().Code);
    }

    [TestMethod]
    public async Task ReadOnlyPreflightListsCandidatesBeforeSelectionAndDoesNotGateSavingLeadsOnTools()
    {
        var service = Service();
        var input = new DownloadIntentRequest {Candidates = [new() {Id = "a", Url = "https://example.test/file.zip"}]};
        var ready = await service.PreflightAsync(input, Path.GetTempPath(), default);
        Assert.AreEqual(1, ready.Candidates.Count);
        Assert.IsTrue(ready.Candidates[0].CanSelect);
        Assert.IsFalse(ready.CanSubmit);
        var leads = await service.PreflightAsync(new() {Mode = "lead", Title = "Saved lead",
            Candidates = [new() {Id = "unknown", Url = "https://example.test/post/42"}], SelectedCandidateIds = ["unknown"]}, null, default);
        Assert.IsTrue(leads.CanSubmit);
        Assert.AreEqual("unsupported", leads.Candidates.Single().State);
    }

    [TestMethod]
    public async Task SubmissionRechecksBlockedInputBeforeTouchingResourceOrRunServices()
    {
        var service = Service();
        var blocked = new DownloadIntentRequest {Title = "A page", Candidates = [new() {Id = "a", Url = "https://example.test/post/42"}], SelectedCandidateIds = ["a"]};
        Assert.IsFalse((await service.PreflightAsync(blocked, Path.GetTempPath(), default)).CanSubmit);
        // Every mutating collaborator is deliberately absent. A blocked request must fail during preflight.
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.SubmitAsync(blocked, Path.GetTempPath(), [], null, default));
    }

    private static DownloadIntentService Service() => new(new([], [], null!, null!), null!,
        new UserStoragePolicy(isContainer: false), null!, null!, null!, null!, null!, null!);

    [DataTestMethod]
    [DataRow("library", null)]
    [DataRow("lead", null)]
    [DataRow("files", 42)]
    public async Task DownloadCenterRejectsAcquisitionGoalsAndCustomWorkflowsBeforeMutation(string mode, int? workflowId)
    {
        var input = new DownloadIntentRequest {Mode = mode, WorkflowDefinitionId = workflowId};
        var service = Service();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.PreflightDownloadsOnlyAsync(input, Path.GetTempPath(), default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SubmitDownloadsOnlyAsync(input, Path.GetTempPath(), default));
    }

    [TestMethod]
    public async Task ExplicitDownloadSourceMustMatchTheAddressAndInvalidPreflightRemainsReadOnly()
    {
        var input = new DownloadIntentRequest
        {
            Candidates = [new() {Id = "one", Url = "https://pan.baidu.com/s/fixture", ProviderId = "googleDrive"}],
            SelectedCandidateIds = ["one"]
        };
        var response = await Service().PreflightDownloadsOnlyAsync(input, Path.GetTempPath(), default);
        Assert.IsFalse(response.CanSubmit);
        Assert.AreEqual("SOURCE_INVALID", response.Candidates.Single().Requirements.Single().Code);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service().SubmitDownloadsOnlyAsync(input, Path.GetTempPath(), default));
    }
}
