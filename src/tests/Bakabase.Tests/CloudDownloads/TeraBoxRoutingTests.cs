using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Service.Components.Workflow;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class TeraBoxRoutingTests
{
    [TestMethod]
    [DataRow("terabox.com")]
    [DataRow("www.terabox.com")]
    [DataRow("1024terabox.com")]
    [DataRow("www.1024terabox.com")]
    public void Reviewed_aliases_share_identity_and_keep_the_original_evidence(string host)
    {
        var url = $"https://{host}/s/1ExampleId?pwd=test";
        var source = DownloadSourceResolver.Normalize(new() {Id = "test", Url = url});
        var canonical = source with {Url = "https://www.terabox.com/s/1ExampleId?pwd=test"};
        Assert.AreEqual("terabox", source.ProviderId);
        Assert.AreEqual(url, source.Url);
        Assert.AreEqual("terabox.cli:v1", source.MethodScope);
        Assert.AreEqual(CloudSourceIdentity.Key(canonical), CloudSourceIdentity.Key(source));
        Assert.AreEqual(AcquisitionDriveKind.TeraBox, AcquisitionDriveKinds.Infer(url));
        Assert.IsTrue(TeraBoxShareLocator.TryParse(url, out var locator));
        Assert.AreEqual("https://www.terabox.com/s/1ExampleId", locator!.CanonicalUrl);
        Assert.AreEqual("test", locator.Password);
    }

    [TestMethod]
    [DataRow("https://terabox.com.attacker.test/s/1ExampleId")]
    [DataRow("https://foo.terabox.com/s/1ExampleId")]
    [DataRow("https://user:secret@terabox.com/s/1ExampleId")]
    [DataRow("https://terabox.com:5000/s/1ExampleId")]
    [DataRow("http://terabox.com/s/1ExampleId")]
    [DataRow("https://terabox.com/s/1ExampleId/child")]
    [DataRow("https://terabox.com/s/1ExampleId#fragment")]
    [DataRow("https://terabox.com/s/1ExampleId?pwd=first&pwd=second")]
    [DataRow("https://terabox.com/s/1ExampleId?pwd=%0A")]
    [DataRow("https://terabox.com/account")]
    public void Other_shapes_are_not_promoted_to_automatic_downloads(string url)
    {
        Assert.IsFalse(TeraBoxShareLocator.TryParse(url, out _));
        Assert.AreEqual("unknown", CloudSourceClassifier.Classify(url));
    }

    [TestMethod]
    public async Task Saved_unknown_share_is_recognized_without_reparsing_or_changing_access_information()
    {
        CloudSource old = new() {Id = "old", ProviderId = "unknown", Url = "https://1024terabox.com/s/1ExampleId",
            ArchivePassword = "archive", AccessCode = "share-code", GroupId = "original-group"};
        var resolved = await new DownloadSourceResolver([]).ResolveAsync(old, default);
        Assert.AreEqual("terabox", resolved.ProviderId);
        Assert.AreEqual(old.Url, resolved.Url);
        Assert.AreEqual(old.AccessCode, resolved.AccessCode);
        Assert.AreEqual(old.ArchivePassword, resolved.ArchivePassword);
        Assert.AreEqual(old.GroupId, resolved.GroupId);
        Assert.IsFalse(DownloadMethodCatalog.Select(resolved)!.SupportsTransfer);
        Assert.AreEqual(ThirdPartyId.TeraBox, CloudSites.Site(resolved.ProviderId));
    }

    [TestMethod]
    public async Task Generic_account_binding_freezes_generation_before_artifact_reuse()
    {
        var resolver = new AccountResolver();
        var registry = new DownloadMethodRegistry([], [], null!, null!, accountResolvers: [resolver]);
        var source = DownloadSourceResolver.Normalize(new() {Id = "s", Url = "https://terabox.com/s/1ExampleId"});
        var bound = await registry.BindAccountAsync(source, null, default);
        Assert.AreEqual("terabox.cli", bound.MethodId);
        Assert.AreEqual(resolver.Id, bound.AccountId);
        Assert.AreEqual("one", bound.AccountVersion);
        var same = await registry.BindAccountAsync(bound, null, default);
        Assert.AreEqual(CloudSourceIdentity.Key(bound), CloudSourceIdentity.Key(same));
        resolver.Generation = "two";
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => registry.BindAccountAsync(bound, null, default));
        var changed = await registry.BindAccountAsync(source, null, default);
        Assert.AreNotEqual(CloudSourceIdentity.Key(bound), CloudSourceIdentity.Key(changed));
    }

    [TestMethod]
    public async Task Integration_limitation_takes_precedence_over_account_setup_and_never_becomes_an_expired_link()
    {
        var account = new AccountResolver {Configured = false};
        var registry = new DownloadMethodRegistry([], [], null!, null!, accountResolvers: [account]);
        var source = DownloadSourceResolver.Normalize(new() {Id = "s", Url = "https://terabox.com/s/1ExampleId"});
        var result = await registry.DescribeAsync(source, null, default);
        Assert.IsFalse(result.Ready);
        Assert.IsTrue(result.Supported);
        Assert.AreEqual("unavailable", result.State);
        Assert.AreEqual(DownloadMethodAvailabilityConstraint.ReasonCode, result.Requirements.Single().Code);
        Assert.AreEqual(0, account.ResolveCalls);
        Assert.AreEqual(SourceAvailabilityStatuses.Unknown,
            ExecutionProblemClassifier.AvailabilityStatus(result.Requirements.Single().Code));
        CollectionAssert.AreEqual(new[] {"planNeedsInput"},
            ExecutionProblemClassifier.Classify(result.Requirements.Select(r => r.Code)).ToArray());
    }
    [TestMethod]
    public async Task An_unregistered_account_resolver_cannot_silently_drop_account_binding()
    {
        var registry = new DownloadMethodRegistry([], [], null!, null!);
        var source = DownloadSourceResolver.Normalize(new() {Id = "s", Url = "https://terabox.com/s/1ExampleId"});
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => registry.BindAccountAsync(source, null, default));
        Assert.AreEqual(DownloadMethodAvailabilityConstraint.ReasonCode,
            (await registry.DescribeAsync(source, null, default)).Requirements.Single().Code);
    }

    [TestMethod]
    [DataRow("TERABOX_SHARE_EXPIRED", SourceAvailabilityStatuses.ConfirmedUnavailable, "sourceUnavailable")]
    [DataRow("TERABOX_SHARE_DELETED", SourceAvailabilityStatuses.ConfirmedUnavailable, "sourceUnavailable")]
    [DataRow("TERABOX_SHARE_UNAVAILABLE", SourceAvailabilityStatuses.Unknown, "sourceNeedsVerification")]
    [DataRow("TERABOX_EMPTY_SHARE", SourceAvailabilityStatuses.Unknown, "sourceNeedsVerification")]
    [DataRow("TERABOX_ACCOUNT_REQUIRED", SourceAvailabilityStatuses.AccessRequired, "accessRequired")]
    [DataRow("TERABOX_PERMISSION_REQUIRED", SourceAvailabilityStatuses.AccessRequired, "accessRequired")]
    [DataRow("TERABOX_NETWORK_UNAVAILABLE", SourceAvailabilityStatuses.TemporarilyUnavailable, "networkIssue")]
    public void Only_explicit_provider_expiry_or_deletion_can_set_the_unavailable_tag(string code, string status, string tag)
    {
        Assert.AreEqual(status, ExecutionProblemClassifier.AvailabilityStatus(code));
        CollectionAssert.AreEqual(new[] {tag}, ExecutionProblemClassifier.Classify([code]).ToArray());
        CollectionAssert.AreEqual(new[] {code}, WorkflowSourceRunProblems.Extract("token_secret " + code + " https://example.invalid/private").ToArray());
    }

    private sealed class AccountResolver : ICloudDownloadAccountResolver
    {
        public string AccountProvider => "terabox";
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Generation { get; set; } = "one";
        public bool Configured { get; init; } = true;
        public int ResolveCalls { get; private set; }
        public Task<CloudAccountReference> ResolveAccountAsync(string? accountId, string? accountVersion, CancellationToken ct)
        {
            ResolveCalls++;
            if (!Configured || accountId != null && accountId != Id || accountVersion != null && accountVersion != Generation)
                throw new InvalidOperationException("TERABOX_ACCOUNT_REQUIRED");
            return Task.FromResult(new CloudAccountReference(Id, Generation));
        }
    }
}
