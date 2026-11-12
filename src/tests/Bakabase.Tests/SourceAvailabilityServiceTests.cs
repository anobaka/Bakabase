using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.SourceAvailability;

namespace Bakabase.Tests;

[TestClass]
public sealed class SourceAvailabilityServiceTests
{
    private string _root = null!;
    private SourceAvailabilityStore _store = null!;
    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "source-availability-tests-" + Guid.NewGuid().ToString("N"));
        _store = new(_root);
    }
    [TestCleanup] public void Cleanup() {if (Directory.Exists(_root)) Directory.Delete(_root, true);}
    private static CloudSource Source(string code = "code") => new() {Id = "candidate", Url = "https://example.org/share", ProviderId = "test", AccessCode = code};
    [TestMethod]
    public async Task ConcurrentChecksShareFreshObservationAndIsolateCredentialAndProbeGenerations()
    {
        var probe = new Probe();
        var service = new SourceAvailabilityService(_store, [probe]);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.CheckAsync(Source(), false, default)));
        Assert.AreEqual(1, probe.Calls);
        Assert.IsTrue(results.All(r => r.Status == SourceAvailabilityStatuses.ConfirmedUnavailable));
        await service.CheckAsync(Source("different"), false, default);
        await service.CheckAsync(Source() with {AccountId = "account"}, false, default);
        await service.CheckAsync(Source() with {AccountId = "account", AccountVersion = "2"}, false, default);
        Assert.AreEqual(4, probe.Calls);
        probe.Version = 2;
        Assert.IsNull(await service.GetAsync(Source(), default));
        await service.CheckAsync(Source(), false, default);
        Assert.AreEqual(5, probe.Calls);
    }
    [TestMethod]
    public async Task DisableIsTaskScopedRestorableAndRejectsStaleDecisionWithoutEditingSource()
    {
        var service = new SourceAvailabilityService(_store, []);
        var source = Source();
        var key = SourceAvailabilityService.DecisionKey("acquisition:1", source);
        Assert.IsTrue(await _store.DecideAsync(key, null, new("first", true, DateTimeOffset.UtcNow, "batch", null), default));
        Assert.IsTrue(await service.IsDisabledAsync("acquisition:1", source, default));
        Assert.IsFalse(await service.IsDisabledAsync("acquisition:2", source, default));
        Assert.IsFalse(await service.IsDisabledAsync("acquisition:1", Source("corrected"), default));
        Assert.IsFalse(await _store.DecideAsync(key, null, new("stale", false, DateTimeOffset.UtcNow, "batch", null), default));
        Assert.IsTrue(await _store.DecideAsync(key, "first", new("second", false, DateTimeOffset.UtcNow, "batch", null), default));
        Assert.IsFalse(await service.IsDisabledAsync("acquisition:1", source, default));
        var files = Directory.GetFiles(Path.Combine(_root, "decisions"));
        var saved = await File.ReadAllTextAsync(files.Single());
        StringAssert.Contains(saved, "first"); StringAssert.Contains(saved, "second");
        Assert.IsFalse(saved.Contains(source.AccessCode!));
        Assert.AreEqual("https://example.org/share", source.Url);
    }
    [TestMethod]
    public async Task ProbeFailureAndExpiredEvidenceCannotAuthorizeDisable()
    {
        var service = new SourceAvailabilityService(_store, [new Probe {Throws = true}]);
        var failed = await service.CheckAsync(Source(), false, default);
        Assert.AreEqual(SourceAvailabilityStatuses.TemporarilyUnavailable, failed.Status);
        Assert.IsFalse(SourceAvailabilityService.IsConfirmedUnavailable(failed));
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(failed).Contains("secret"));
        Assert.IsFalse(SourceAvailabilityService.IsConfirmedUnavailable(failed with {Status = SourceAvailabilityStatuses.ConfirmedUnavailable, ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1)}));
    }
    [TestMethod]
    public async Task Default_account_generation_is_bound_before_caching_an_authenticated_observation()
    {
        var probe = new Probe();
        var accounts = new AccountResolver();
        var service = new SourceAvailabilityService(_store, [probe], accountResolvers: [accounts]);
        CloudSource source = new() {Id = "original", ProviderId = "terabox", Url = "https://terabox.com/s/1ExampleId"};
        await service.CheckAsync(source, false, default);
        Assert.AreEqual(1, probe.Calls);
        Assert.IsNotNull(await service.GetAsync(source, default));
        accounts.Generation = "second";
        Assert.IsNull(await service.GetAsync(source, default));
        await service.CheckAsync(source, false, default);
        Assert.AreEqual(2, probe.Calls);
        Assert.IsNull(source.AccountId, "Binding a probe must not mutate the original source or its history.");
    }
    private sealed class AccountResolver : ICloudDownloadAccountResolver
    {
        public string AccountProvider => "terabox";
        public string Generation {get; set;} = "first";
        public Task<CloudAccountReference> ResolveAccountAsync(string? accountId, string? accountVersion, CancellationToken ct) =>
            Task.FromResult(new CloudAccountReference("configured-account", Generation));
    }
    private sealed class Probe : ISourceAvailabilityProbe
    {
        public int Calls;
        public int Version = 1;
        public bool Throws;
        public int CacheVersion => Version;
        public bool UsesAccountScope => true;
        public bool CanHandle(CloudSource source) => true;
        public async Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls); await Task.Delay(10, ct);
            if (Throws) throw new InvalidOperationException("secret code=123");
            return new() {Status = SourceAvailabilityStatuses.ConfirmedUnavailable, ReasonCode = "PROVIDER_REMOVED", ProbeId = "test", ProbeVersion = Version,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)};
        }
    }
}
