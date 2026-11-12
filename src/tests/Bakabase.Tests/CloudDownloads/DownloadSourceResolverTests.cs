using System.Net;
using System.Net.Http.Headers;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.DownloadIntents;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class DownloadSourceResolverTests
{
    [DataTestMethod]
    [DataRow("https://pixeldra.in/u/file-id", "pixeldrain", "pixeldrain.galleryDl")]
    [DataRow("https://pixeldrain.net/u/file-id", "pixeldrain", "pixeldrain.galleryDl")]
    [DataRow("https://pan.baidu.com/s/share", "baidu", "baidu.pcs")]
    public async Task Unknown_provider_placeholder_is_inferred_without_changing_source_evidence(
        string url, string provider, string method)
    {
        var candidate = new DownloadCandidate {Id = "saved", Url = url, Kind = "share", ProviderId = "unknown",
            AccessCode = "saved-code", ArchivePassword = "saved-password"};
        var normalized = DownloadSourceResolver.Normalize(candidate);
        var resolved = await new DownloadSourceResolver([]).ResolveAsync(candidate, default);
        Assert.AreEqual(normalized.ProviderId, resolved.ProviderId);
        Assert.AreEqual(normalized.Url, resolved.Url);
        Assert.AreEqual(normalized.MethodId, resolved.MethodId);
        Assert.AreEqual(provider, normalized.ProviderId);
        Assert.AreEqual(method, DownloadMethodCatalog.Select(normalized)!.Id);
        Assert.AreEqual(url, normalized.Url);
        Assert.AreEqual(candidate.AccessCode, normalized.AccessCode);
        Assert.AreEqual(candidate.ArchivePassword, normalized.ArchivePassword);
        Assert.AreEqual("unknown", candidate.ProviderId, "Reading a plan must preserve the saved evidence.");
    }

    [DataTestMethod]
    [DataRow("https://pixeldra.in/u/file-id", "baidu", null)]
    [DataRow("https://pan.baidu.com/s/share", "pixeldrain", null)]
    [DataRow("https://pixeldra.in/u/file-id", "unknown", "baidu.pcs")]
    [DataRow("https://pan.baidu.com/s/share", "unknown", "pixeldrain.galleryDl")]
    public void Unknown_placeholder_does_not_relax_explicit_provider_or_method_constraints(
        string url, string provider, string? method) =>
        Assert.ThrowsExactly<ArgumentException>(() => DownloadSourceResolver.Normalize(new()
            {Id = "saved", Url = url, Kind = "share", ProviderId = provider, MethodId = method}));

    [TestMethod]
    public void Unknown_provider_cannot_infer_an_account_path() =>
        Assert.ThrowsExactly<ArgumentException>(() => DownloadSourceResolver.Normalize(new()
            {Id = "saved", Url = "/folder", Kind = "remote", ProviderId = "unknown"}));

    [TestMethod]
    public async Task Pure_source_coordinator_requires_no_account_dependency_or_download_provider()
    {
        using var services = new ServiceCollection().AddSingleton<DownloadSourceResolver>().BuildServiceProvider();
        var coordinator = services.GetRequiredService<DownloadSourceResolver>();
        var source = await coordinator.ResolveAsync(new DownloadCandidate
            {Id = "one", Url = "https://pan.baidu.com/s/share", MethodId = "baidu.pcs"}, default);
        Assert.AreEqual("baidu", source.ProviderId);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => coordinator.ResolveAsync(new DownloadCandidate
            {Id = "one", Url = "https://pan.baidu.com/s/share", ProviderId = "pikpak"}, default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => coordinator.ResolveAsync(source with
            {OriginalUrl = "https://cm1.hk/s/first", ResolutionChain = ["https://cm1.hk/s/first"]}, default));
    }

    [DataTestMethod]
    [DataRow("unknown", "share", "https://unreviewed.example/post/1")]
    [DataRow("baidu", "remote", "/own-folder")]
    [DataRow("baidu", "share", "https://pan.baidu.com/s/share")]
    public async Task Unchanged_source_retains_its_identity_after_validation(string provider, string kind, string url)
    {
        var coordinator = new DownloadSourceResolver([]);
        var source = new CloudSource {Id = "one", ProviderId = provider, Kind = kind, Url = url,
            AccessCode = "saved-code", ArchivePassword = "archive-password", GroupId = "group"};
        var resolved = await coordinator.ResolveAsync(source, default);
        Assert.AreSame(source, resolved);
        Assert.AreSame(source.ResolutionChain, resolved.ResolutionChain);
        if (kind != "remote")
        {
            var untrimmed = source with {Url = " " + url + " "};
            var normalized = await coordinator.ResolveAsync(untrimmed, default);
            Assert.AreEqual(url, normalized.Url, "Retaining unchanged inputs must not bypass normalization.");
            Assert.AreSame(source.ResolutionChain, normalized.ResolutionChain);
        }
    }

    [TestMethod]
    public async Task Expansion_preserves_provenance_and_uses_the_final_share_for_identity_and_readiness()
    {
        using var transport = new RecordingHandler(_ => Redirect("https://mypikpak.com/s/final?pwd=code"));
        using var resolver = Resolver(transport);
        var registry = Registry(resolver);
        var source = await registry.ResolveAsync(new DownloadCandidate
        {
            Id = "candidate", Url = "https://cm1.hk/s/original", ProviderId = "pikpak", MethodId = "pikpak.share",
            ArchivePassword = "archive-password", GroupId = "group"
        }, default);
        Assert.AreEqual("pikpak", source.ProviderId);
        Assert.AreEqual("https://mypikpak.com/s/final?pwd=code", source.Url);
        Assert.AreEqual("https://cm1.hk/s/original", source.OriginalUrl);
        CollectionAssert.AreEqual(new[] {source.OriginalUrl, source.Url}, source.ResolutionChain);
        Assert.AreEqual("archive-password", source.ArchivePassword);
        Assert.AreEqual("group", source.GroupId);
        Assert.AreEqual(1, transport.Requests.Count, "A provider share is not scraped by the short-link resolver.");
        var direct = DownloadMethodRegistry.Normalize(new() {Id = "other", Url = source.Url});
        Assert.AreEqual(CloudSourceIdentity.Key(direct), CloudSourceIdentity.Key(source));
        var readiness = await registry.DescribeAsync(source, "unrelated-account", default);
        Assert.IsFalse(readiness.Ready);
        Assert.IsTrue(readiness.Requirements.Any(r => r.Code == "PIKPAK_ACCOUNT_REQUIRED"),
            "Resolving a public share must not imply anonymous original-file download access.");
        var resumed = await registry.ResolveAsync(source, default);
        Assert.AreEqual(source, resumed);
        Assert.AreEqual(1, transport.Requests.Count, "Resuming must not retarget an already fixed operation.");
    }

    [TestMethod]
    public async Task Unknown_pages_are_not_fetched_or_reinterpreted_as_files()
    {
        using var transport = new RecordingHandler(_ => throw new AssertFailedException("Unknown hosts must not be fetched."));
        using var resolver = Resolver(transport);
        var registry = Registry(resolver);
        var source = await registry.ResolveAsync(new DownloadCandidate {Id = "a", Url = "https://unreviewed.example/post/1"}, default);
        Assert.AreEqual("unknown", source.ProviderId);
        Assert.IsFalse((await registry.DescribeAsync(source, null, default)).Supported);
        Assert.AreEqual(0, transport.Requests.Count);
    }

    [DataTestMethod]
    [DataRow("http://mypikpak.com/s/final")]
    [DataRow("https://user:secret@mypikpak.com/s/final")]
    [DataRow("https://127.0.0.1/file.zip")]
    [DataRow("https://169.254.169.254/file.zip")]
    [DataRow("https://[::1]/file.zip")]
    [DataRow("https://8.8.8.8/file.zip")]
    [DataRow("https://localhost/file.zip")]
    [DataRow("https://mypikpak.com:8443/s/final")]
    [DataRow("file:///etc/passwd")]
    [DataRow("https://cm1.hk/s/original")]
    public async Task Unsafe_or_cyclic_redirects_stop_before_the_destination_is_requested(string destination)
    {
        using var transport = new RecordingHandler(_ => Redirect(destination));
        using var resolver = Resolver(transport);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => resolver.ResolveAsync(new("https://cm1.hk/s/original"), default));
        Assert.AreEqual(1, transport.Requests.Count);
    }

    [TestMethod]
    public async Task Redirects_cannot_hide_a_private_DNS_answer()
    {
        using var transport = new RecordingHandler(_ => Redirect("https://mypikpak.com/s/final"));
        using var resolver = new HttpRedirectSourceResolver(new HttpClient(transport), (host, _) =>
            Task.FromResult(host == "cm1.hk" ? new[] {IPAddress.Parse("8.8.8.8")} :
                new[] {IPAddress.Parse("8.8.4.4"), IPAddress.Parse("10.0.0.1")}));
        await Assert.ThrowsExactlyAsync<CloudSourceNetworkException>(() => resolver.ResolveAsync(new("https://cm1.hk/s/original"), default));
        Assert.AreEqual(1, transport.Requests.Count);
    }

    [TestMethod]
    public async Task Expansion_has_a_bounded_hop_count()
    {
        var sequence = 0;
        using var transport = new RecordingHandler(_ => Redirect("https://cm1.hk/s/next" + ++sequence));
        using var resolver = Resolver(transport);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => resolver.ResolveAsync(new("https://cm1.hk/s/original"), default));
        Assert.AreEqual(HttpRedirectSourceResolver.MaximumRedirects + 1, transport.Requests.Count);
    }

    [DataTestMethod]
    [DataRow("https://cm1.hk/s/original", "text/html")]
    [DataRow("https://cm2.hk/s/another", "text/html")]
    [DataRow("https://cm2.hk/s/another", "application/xhtml+xml")]
    public async Task Html_terminal_responses_never_become_a_download(string destination, string contentType)
    {
        using var transport = new RecordingHandler(uri => uri.AbsoluteUri == "https://cm1.hk/s/original" &&
            destination != uri.AbsoluteUri ? Redirect(destination) : Content(contentType));
        using var resolver = Resolver(transport);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => resolver.ResolveAsync(new("https://cm1.hk/s/original"), default));
    }

    [DataTestMethod]
    [DataRow("https://files.example/archive.zip?signature=meaningful", "unknown")]
    [DataRow("https://files.example/archive.torrent", "unknown")]
    [DataRow("https://files.example/share/page", "unknown")]
    [DataRow("https://www.feemoo.com/s/final", "feemoo")]
    public async Task An_unreviewed_terminal_page_is_preserved_but_never_fetched_or_approved(string destination, string provider)
    {
        using var transport = new RecordingHandler(uri => uri.IdnHost == "cm1.hk"
            ? Redirect(destination) : throw new AssertFailedException("The terminal page must not be fetched."));
        using var resolver = Resolver(transport);
        var registry = Registry(resolver);
        var candidate = new DownloadCandidate {Id = "a", Url = "https://cm1.hk/s/original"};
        var preflight = await Service(registry).PreflightAsync(new()
            {Candidates = [candidate], SelectedCandidateIds = ["a"]}, Path.GetTempPath(), default);
        var view = preflight.Candidates.Single();
        Assert.IsFalse(preflight.CanSubmit);
        Assert.AreEqual("unsupported", view.State);
        Assert.AreEqual("SOURCE_UNSUPPORTED", view.Requirements.Single().Code);
        var source = view.ResolvedSource!;
        Assert.AreEqual(provider, source.ProviderId);
        Assert.AreEqual("share", source.Kind);
        Assert.AreEqual(destination, source.Url);
        Assert.AreEqual(candidate.Url, source.OriginalUrl);
        CollectionAssert.AreEqual(new[] {candidate.Url, destination}, source.ResolutionChain);
        var resumed = await registry.ResolveAsync(source, default);
        Assert.AreEqual(source, resumed);
        Assert.IsFalse((await registry.DescribeAsync(resumed, null, default)).Supported);
        Assert.AreEqual(1, transport.Requests.Count);
    }

    [TestMethod]
    public async Task Explicit_proxy_owns_DNS_but_does_not_fetch_unreviewed_destinations()
    {
        var destination = "https://mypikpak.com/s/final";
        using var transport = new RecordingHandler(_ => Redirect(destination));
        using var resolver = new HttpRedirectSourceResolver(new HttpClient(transport),
            (_, _) => throw new AssertFailedException("An explicit proxy resolves the reviewed domain."),
            _ => true);
        Assert.AreEqual(destination, (await resolver.ResolveAsync(new("https://cm1.hk/s/original"), default)).Url);
        destination = "https://unreviewed.example/file.zip";
        Assert.AreEqual(destination, (await resolver.ResolveAsync(new("https://cm1.hk/s/original"), default)).Url);
        Assert.AreEqual(2, transport.Requests.Count);
    }

    [TestMethod]
    public async Task Direct_fake_DNS_is_a_configuration_requirement_not_a_ready_source()
    {
        using var transport = new RecordingHandler(_ => throw new AssertFailedException("Private DNS must not be connected."));
        using var resolver = new HttpRedirectSourceResolver(new HttpClient(transport),
            (_, _) => Task.FromResult(new[] {IPAddress.Parse("198.18.1.1")}));
        var preflight = await Service(Registry(resolver)).PreflightAsync(new()
        {
            Candidates = [new() {Id = "a", Url = "https://cm1.hk/s/original"}], SelectedCandidateIds = ["a"]
        }, Path.GetTempPath(), default);
        Assert.IsFalse(preflight.CanSubmit);
        Assert.AreEqual("needsSetup", preflight.Candidates[0].State);
        Assert.AreEqual(CloudSourceNetworkException.RequirementCode, preflight.Candidates[0].Requirements.Single().Code);
    }
    [TestMethod]
    public async Task Preflight_and_submission_resolve_original_input_and_reject_a_changed_target()
    {
        var destination = "https://tenant.sharepoint.com/:f:/s/test/final";
        using var transport = new RecordingHandler(_ => Redirect(destination));
        using var resolver = Resolver(transport);
        var service = Service(Registry(resolver));
        var input = new DownloadIntentRequest
        {
            Candidates = [new() {Id = "a", Url = "https://cm1.hk/s/original", ProviderId = "oneDrive", MethodId = "oneDrive.sharePoint"}],
            SelectedCandidateIds = ["a"]
        };
        var preflight = await service.PreflightAsync(input, Path.GetTempPath(), default);
        Assert.IsTrue(preflight.CanSubmit);
        Assert.AreEqual(input.Candidates[0].Url, preflight.Candidates[0].Candidate.Url);
        Assert.AreEqual(destination, preflight.Candidates[0].ResolvedSource!.Url);
        destination = "https://unreviewed.example/login";
        // Mutating collaborators are absent: submission must fail during its repeated resolution.
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.SubmitAsync(input, Path.GetTempPath(), [], null, default));
        Assert.AreEqual(2, transport.Requests.Count);
    }

    [TestMethod]
    public async Task Saving_a_lead_does_not_require_short_link_resolution()
    {
        using var transport = new RecordingHandler(_ => throw new AssertFailedException("Saving leads must not probe links."));
        using var resolver = Resolver(transport);
        var result = await Service(Registry(resolver)).PreflightAsync(new()
        {
            Mode = "lead", Title = "A source", Candidates = [new() {Id = "a", Url = "https://cm1.hk/s/original"}],
            SelectedCandidateIds = ["a"]
        }, null, default);
        Assert.IsTrue(result.CanSubmit);
        Assert.AreEqual("https://cm1.hk/s/original", result.Candidates[0].ResolvedSource!.Url);
        Assert.AreEqual(0, transport.Requests.Count);
    }

    [DataTestMethod]
    [DataRow("10.0.0.1")]
    [DataRow("100.64.0.1")]
    [DataRow("127.0.0.1")]
    [DataRow("169.254.169.254")]
    [DataRow("192.168.1.1")]
    [DataRow("192.0.2.1")]
    [DataRow("198.18.0.1")]
    [DataRow("::1")]
    [DataRow("::ffff:127.0.0.1")]
    [DataRow("fe80::1")]
    [DataRow("fc00::1")]
    [DataRow("2001:db8::1")]
    public void Public_network_policy_rejects_local_reserved_and_mapped_addresses(string address) =>
        Assert.IsFalse(PublicCloudHttp.IsPublicAddress(IPAddress.Parse(address)));

    private static DownloadIntentService Service(DownloadMethodRegistry registry) => new(registry, null!,
        new UserStoragePolicy(false), null!, null!, null!, null!, null!, null!);
    private static DownloadMethodRegistry Registry(HttpRedirectSourceResolver resolver) =>
        new([new PublicProvider("pikpak"), new PublicProvider("oneDrive")], [], null!, null!, new([resolver]));
    private static HttpRedirectSourceResolver Resolver(HttpMessageHandler handler) =>
        new(new HttpClient(handler), (_, _) => Task.FromResult(new[] {IPAddress.Parse("8.8.8.8")}));
    private static HttpResponseMessage Redirect(string target) => new(HttpStatusCode.Redirect)
        {Headers = {Location = new Uri(target, UriKind.Absolute)}};
    private static HttpResponseMessage Content(string contentType) => new(HttpStatusCode.OK)
        {Content = new StringContent("") {Headers = {ContentType = new MediaTypeHeaderValue(contentType)}}};

    private sealed class RecordingHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests {get;} = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request.RequestUri!));
        }
    }
    private sealed class PublicProvider(string id) : ICloudDownloadProvider
    {
        public string ProviderId => id;
        public Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct)
        {
            Assert.IsNull(accountId, "Public shares must not receive an unrelated account reference.");
            return Task.FromResult(new CloudReadiness(true));
        }
        public Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request, Func<CloudDownloadProgress, Task> progress, CancellationToken ct) =>
            throw new AssertFailedException("Preflight must never download files.");
    }
}
