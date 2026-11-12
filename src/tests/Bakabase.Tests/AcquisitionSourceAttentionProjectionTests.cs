using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.SourceAvailability;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionSourceAttentionProjectionTests
{
    private string _root = null!;
    private SourceAvailabilityStore _store = null!;
    private SourceAvailabilityService _availability = null!;
    private readonly Probe _probe = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [TestInitialize] public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "acquisition-source-projection-" + Guid.NewGuid().ToString("N"));
        _store = new(_root); _availability = new(_store, [_probe]);
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private static AcquisitionPlanSource Source(string id) => new() {Id = id, Url = "https://gofile.io/d/" + id, ProviderId = "gofile"};
    private static AcquisitionPlan Plan(params AcquisitionPlanSource[] sources) => new()
    {Parts = [new() {Id = "part", Sources = sources.ToList(), SelectedSourceId = sources[0].Id}]};
    private async Task Check(AcquisitionPlanSource source, string status, string code, bool expired = false)
    {
        _probe.Result = new() {Status = status, ReasonCode = code, ProbeId = "test",
            CheckedAt = DateTimeOffset.UtcNow.AddMinutes(expired ? -10 : 0),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(expired ? -5 : 10)};
        await _availability.CheckAsync(DownloadMethodRegistry.Normalize(new DownloadCandidate
            {Id = source.Id, Url = source.Url, ProviderId = source.ProviderId, Kind = source.Kind}), true, default);
    }
    private Task<IReadOnlyList<string>> Project(AcquisitionPlan plan, string[] codes,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? parts = null, DateTimeOffset? at = null,
        IReadOnlySet<string>? acquired = null, string? activityKind = null) =>
        new AcquisitionSourceAttentionProjection(_availability).ProjectAsync(JsonSerializer.Serialize(plan, Json), codes,
            parts ?? new Dictionary<string, IReadOnlyList<string>> { ["part"] = codes }, at, default, acquired, activityKind);

    [DataTestMethod]
    [DataRow("SOURCE_ATTENTION_REQUIRED")]
    [DataRow(null)]
    public async Task FreshCheckFillsLegacyGenericOrUnrecognizedSourceDiagnosis(string? oldCode)
    {
        var source = Source("first1"); var plan = Plan(source);
        var codes = oldCode == null ? Array.Empty<string>() : new[] {oldCode};
        await Check(source, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND");
        CollectionAssert.AreEqual(new[] {"SOURCE_SHARE_NOT_FOUND"},
            (await Project(plan, codes, activityKind: "baidu.transfer")).ToArray());
        await Check(source, SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
        CollectionAssert.AreEqual(new[] {"SOURCE_CHECK_PASSED_RETRY_REQUIRED"},
            (await Project(plan, codes, activityKind: "download.enqueueSupported")).ToArray());
        Assert.AreEqual(2, _probe.Calls, "Projection must not issue another probe.");
    }

    [TestMethod]
    public async Task LegacyGenericDiagnosisUsesNewerDefinitiveEvidenceWithoutLosingHistoricalAbsence()
    {
        var source = Source("first1"); var plan = Plan(source);
        foreach (var codes in new[] {Array.Empty<string>(), new[] {"SOURCE_ATTENTION_REQUIRED"}})
        {
            await Check(source, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND", expired: true);
            CollectionAssert.AreEqual(new[] {"SOURCE_SHARE_NOT_FOUND"},
                (await Project(plan, codes, at: DateTimeOffset.UtcNow.AddMinutes(-20), activityKind: "baidu.transfer")).ToArray());
            var historical = await _availability.GetAsync(DownloadMethodRegistry.Normalize(new DownloadCandidate
                {Id = source.Id, Url = source.Url, ProviderId = source.ProviderId}), default);
            Assert.IsFalse(SourceAvailabilityService.IsConfirmedUnavailable(historical),
                "A historical display tag must not authorize a disable or source switch.");
            await Check(source, SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE", expired: true);
            CollectionAssert.AreEqual(codes, (await Project(plan, codes, activityKind: "baidu.transfer")).ToArray());
            await Check(source, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND");
            CollectionAssert.AreEqual(codes, (await Project(plan, codes, at: DateTimeOffset.UtcNow.AddMinutes(1),
                activityKind: "baidu.transfer")).ToArray());
            foreach (var status in new[] {SourceAvailabilityStatuses.Unknown, SourceAvailabilityStatuses.TemporarilyUnavailable,
                         SourceAvailabilityStatuses.AccessRequired, SourceAvailabilityStatuses.Unsupported})
            {
                await Check(source, status, "SOURCE_PROBE_PROTOCOL_CHANGED");
                CollectionAssert.AreEqual(codes, (await Project(plan, codes, activityKind: "baidu.transfer")).ToArray());
            }
        }
    }

    [TestMethod]
    public async Task GenericSourceProjectionCannotReplaceOtherBlockersOrGuessOtherStages()
    {
        var source = Source("first1"); await Check(source, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND");
        foreach (var blocker in new[] {"BAIDU_ACCOUNT_INVALID", "BAIDU_LOGIN_REQUIRED", "DEPENDENCY_REQUIRED",
                     "PROCESSING_PLAN_INCOMPLETE", "PUBLICATION_IO_ERROR", "SOME_UNRECOGNIZED_FAILURE"})
        {
            var codes = new[] {"SOURCE_ATTENTION_REQUIRED", blocker};
            CollectionAssert.AreEqual(codes, (await Project(Plan(source), codes, activityKind: "baidu.transfer")).ToArray());
        }
        foreach (var kind in new string?[] {null, "acquisition.plan.process", "download.processFiles", "fs.publishArtifacts", "unknown.activity"})
            foreach (var codes in new[] {Array.Empty<string>(), new[] {"SOURCE_ATTENTION_REQUIRED"}})
                CollectionAssert.AreEqual(codes, (await Project(Plan(source), codes, activityKind: kind)).ToArray());
        CollectionAssert.AreEqual(new[] {"SOURCE_ATTENTION_REQUIRED"}, (await Project(Plan(source), ["SOURCE_ATTENTION_REQUIRED"],
            acquired: new HashSet<string> {"part"}, activityKind: "baidu.transfer")).ToArray());
    }

    [TestMethod]
    public async Task LegacyGenericDiagnosisStillRequiresAllMirrorsAndAnUnambiguousPart()
    {
        var first = Source("first1"); var second = Source("second");
        await Check(first, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND");
        CollectionAssert.AreEqual(new[] {"SOURCE_VALIDITY_UNKNOWN"}, (await Project(Plan(first, second),
            ["SOURCE_ATTENTION_REQUIRED"], activityKind: "baidu.transfer")).ToArray());
        await Check(second, SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
        CollectionAssert.AreEqual(new[] {"SOURCE_SELECTION_REQUIRED"}, (await Project(Plan(first, second),
            ["SOURCE_ATTENTION_REQUIRED"], activityKind: "baidu.transfer")).ToArray());
        var multiPart = Plan(first) with {Parts = [Plan(first).Parts[0], new() {Id = "other", Sources = [second]}]};
        CollectionAssert.AreEqual(new[] {"SOURCE_ATTENTION_REQUIRED"}, (await Project(multiPart, ["SOURCE_ATTENTION_REQUIRED"],
            new Dictionary<string, IReadOnlyList<string>>(), activityKind: "baidu.transfer")).ToArray());
        CollectionAssert.AreEquivalent(new[] {"SOURCE_SHARE_NOT_FOUND", "SOURCE_ATTENTION_REQUIRED"},
            (await Project(Plan(first), ["SOURCE_ATTENTION_REQUIRED"], new Dictionary<string, IReadOnlyList<string>>
                {["part"] = ["SOURCE_ATTENTION_REQUIRED"], ["unknown-part"] = ["SOURCE_ATTENTION_REQUIRED"]},
                activityKind: "baidu.transfer")).ToArray());
    }

    [TestMethod]
    public async Task UnboundLegacyMultiPartFailureRequiresEveryPartAndMirrorExplicitlyUnavailable()
    {
        var first = Source("first1"); var second = Source("second"); var mirror = Source("mirror");
        var plan = Plan(first) with {Parts = [Plan(first).Parts[0],
            new() {Id = "other", Sources = [second, mirror], SelectedSourceId = second.Id}]};
        var unbound = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var codes in new[] {Array.Empty<string>(), new[] {"SOURCE_ATTENTION_REQUIRED"}})
        {
            await Check(first, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND");
            await Check(second, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND");
            await Check(mirror, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND", expired: true);
            CollectionAssert.AreEqual(new[] {"SOURCE_CONFIRMED_UNAVAILABLE"},
                (await Project(plan, codes, unbound, at: DateTimeOffset.UtcNow.AddMinutes(-20), activityKind: "baidu.transfer")).ToArray());
            CollectionAssert.AreEqual(codes,
                (await Project(plan, codes, unbound, at: DateTimeOffset.UtcNow.AddMinutes(-2), activityKind: "baidu.transfer")).ToArray());
            await Check(mirror, SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
            CollectionAssert.AreEqual(codes, (await Project(plan, codes, unbound, activityKind: "baidu.transfer")).ToArray());
            await Check(mirror, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND");
            CollectionAssert.AreEqual(new[] {"SOURCE_CONFIRMED_UNAVAILABLE"},
                (await Project(plan, codes, unbound, activityKind: "baidu.transfer")).ToArray());
            CollectionAssert.AreEqual(codes, (await Project(plan, codes, unbound, acquired: new HashSet<string> {"part"},
                activityKind: "baidu.transfer")).ToArray());
            var continued = plan with {Parts = [plan.Parts[0] with {ContinueFrom = new(1, 1, "artifact", "operation", "fingerprint")}, plan.Parts[1]]};
            CollectionAssert.AreEqual(codes, (await Project(continued, codes, unbound, activityKind: "baidu.transfer")).ToArray());
        }
    }

    [TestMethod] public async Task FreshProviderAbsenceUpdatesOldAmbiguousErrorWithoutAnotherProbeOrPlanWrite()
    {
        var source = Source("first1"); var plan = Plan(source);
        await Check(source, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND");
        var result = await Project(plan, ["GOFILE_SHARE_NOT_FOUND"]);
        CollectionAssert.AreEqual(new[] {"SOURCE_SHARE_NOT_FOUND"}, result.ToArray());
        Assert.AreEqual(1, _probe.Calls);
        Assert.AreEqual("https://gofile.io/d/first1", plan.Parts[0].Sources[0].Url);
    }
    [TestMethod] public async Task OneDeadMirrorDoesNotDeclareWholePartUnavailable()
    {
        var first = Source("first1"); var second = Source("second"); var plan = Plan(first, second);
        await Check(first, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND");
        CollectionAssert.AreEqual(new[] {"SOURCE_VALIDITY_UNKNOWN"}, (await Project(plan, ["GOFILE_SHARE_NOT_FOUND"])).ToArray());
        await Check(second, SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
        CollectionAssert.AreEqual(new[] {"SOURCE_SELECTION_REQUIRED"}, (await Project(plan, ["GOFILE_SHARE_NOT_FOUND"])).ToArray());
        await Check(second, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND");
        CollectionAssert.AreEqual(new[] {"SOURCE_CONFIRMED_UNAVAILABLE"}, (await Project(plan, ["GOFILE_SHARE_NOT_FOUND"])).ToArray());
    }
    [TestMethod] public async Task LaterSuccessfulCheckClearsOldSourceFailureButPreservesOtherRequirements()
    {
        var source = Source("first1"); await Check(source, SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
        var result = await Project(Plan(source), ["BAIDU_SHARE_EXPIRED", "DEPENDENCY_REQUIRED", "SOURCE_AUTH_REQUIRED"]);
        CollectionAssert.AreEquivalent(new[] {"SOURCE_CHECK_PASSED_RETRY_REQUIRED", "DEPENDENCY_REQUIRED", "SOURCE_AUTH_REQUIRED"}, result.ToArray());
    }
    [TestMethod] public async Task ExpiredConfirmedAbsenceRemainsHistoricalDisplayEvidenceButCannotAuthorizeActions()
    {
        var source = Source("first1"); await Check(source, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND", expired: true);
        CollectionAssert.AreEqual(new[] {"SOURCE_SHARE_NOT_FOUND"}, (await Project(Plan(source), ["GOFILE_SHARE_NOT_FOUND"])).ToArray());
        var observation = await _availability.GetAsync(DownloadMethodRegistry.Normalize(new DownloadCandidate
            {Id = source.Id, Url = source.Url, ProviderId = source.ProviderId}), default);
        Assert.IsFalse(SourceAvailabilityService.IsConfirmedUnavailable(observation));
        Assert.AreEqual(1, _probe.Calls);
    }
    [TestMethod] public async Task ExpiredSuccessOrAnyOlderCheckCannotReplaceCurrentExecutionEvidence()
    {
        var source = Source("first1"); await Check(source, SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE", expired: true);
        CollectionAssert.AreEqual(new[] {"BAIDU_SHARE_EXPIRED"}, (await Project(Plan(source), ["BAIDU_SHARE_EXPIRED"])).ToArray());
        await Check(source, SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
        CollectionAssert.AreEqual(new[] {"BAIDU_SHARE_EXPIRED"}, (await Project(Plan(source), ["BAIDU_SHARE_EXPIRED"], at: DateTimeOffset.UtcNow.AddMinutes(1))).ToArray());
        await Check(source, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND", expired: true);
        CollectionAssert.AreEqual(new[] {"GOFILE_SHARE_NOT_FOUND"}, (await Project(Plan(source), ["GOFILE_SHARE_NOT_FOUND"], at: DateTimeOffset.UtcNow)).ToArray());
    }
    [TestMethod] public async Task HistoricalUnavailableMirrorDoesNotHideUncheckedAlternatives()
    {
        var first = Source("first1"); var second = Source("second");
        await Check(first, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND", expired: true);
        CollectionAssert.AreEqual(new[] {"SOURCE_VALIDITY_UNKNOWN"}, (await Project(Plan(first, second), ["GOFILE_SHARE_NOT_FOUND"])).ToArray());
        await Check(second, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND", expired: true);
        CollectionAssert.AreEqual(new[] {"SOURCE_CONFIRMED_UNAVAILABLE"}, (await Project(Plan(first, second), ["GOFILE_SHARE_NOT_FOUND"])).ToArray());
    }
    [TestMethod] public async Task SourceNetworkAndShareGatesCanRefreshWithoutClearingAccountRequirements()
    {
        var source = Source("first1"); await Check(source, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND");
        foreach (var code in new[] {"BAIDU_SHARE_TEMPORARILY_UNAVAILABLE", "SOURCE_SHARE_TEMPORARILY_UNAVAILABLE",
                     "CLOUD_NETWORK_ERROR", "SOURCE_RATE_OR_SERVICE_LIMIT", "BAIDU_SHARE_FROZEN", "SOURCE_SHARE_ACCESS_RESTRICTED"})
            CollectionAssert.AreEqual(new[] {"SOURCE_SHARE_NOT_FOUND"}, (await Project(Plan(source), [code])).ToArray());
        CollectionAssert.AreEquivalent(new[] {"SOURCE_SHARE_NOT_FOUND", "BAIDU_ACCOUNT_INVALID", "BAIDU_LOGIN_REQUIRED"},
            (await Project(Plan(source), ["BAIDU_SHARE_UNAVAILABLE", "BAIDU_ACCOUNT_INVALID", "BAIDU_LOGIN_REQUIRED"])).ToArray());
        await Check(source, SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
        CollectionAssert.AreEqual(new[] {"SOURCE_CHECK_PASSED_RETRY_REQUIRED"}, (await Project(Plan(source), ["BAIDU_SHARE_FROZEN"])).ToArray());
        CollectionAssert.AreEqual(new[] {"BAIDU_ACCOUNT_INVALID"}, (await Project(Plan(source), ["BAIDU_ACCOUNT_INVALID"])).ToArray());
    }
    [TestMethod] public async Task AcquiredPartDoesNotBackfillDeadOriginalLinkWhileProcessing()
    {
        var source = Source("first1"); await Check(source, SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND");
        CollectionAssert.AreEquivalent(new[] {"GOFILE_SHARE_NOT_FOUND", "PUBLICATION_IO_ERROR"},
            (await Project(Plan(source), ["GOFILE_SHARE_NOT_FOUND", "PUBLICATION_IO_ERROR"], acquired: new HashSet<string> {"part"})).ToArray());
        Assert.AreEqual(1, _probe.Calls);
    }
    [TestMethod] public async Task UnknownCheckDoesNotErasePreviouslyConfirmedAbsence()
    {
        var source = Source("first1"); await Check(source, SourceAvailabilityStatuses.Unknown, "SOURCE_PROBE_PROTOCOL_CHANGED");
        CollectionAssert.AreEqual(new[] {"BAIDU_SHARE_EXPIRED"}, (await Project(Plan(source), ["BAIDU_SHARE_EXPIRED"])).ToArray());
    }
    [TestMethod] public async Task UnboundMultiPartDiagnosticsCannotBeAttributedToAlreadyCompletedParts()
    {
        var source = Source("first1"); var plan = Plan(source) with {Parts = [Plan(source).Parts[0], new() {Id = "other", Sources = [Source("second")]}]};
        await Check(source, SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
        CollectionAssert.AreEqual(new[] {"GOFILE_SHARE_NOT_FOUND"}, (await Project(plan, ["GOFILE_SHARE_NOT_FOUND"], new Dictionary<string, IReadOnlyList<string>>())).ToArray());
        var projected = await Project(plan, ["GOFILE_SHARE_NOT_FOUND", "BAIDU_SHARE_EXPIRED"],
            new Dictionary<string, IReadOnlyList<string>> { ["part"] = new[] {"GOFILE_SHARE_NOT_FOUND"} });
        CollectionAssert.AreEquivalent(new[] {"BAIDU_SHARE_EXPIRED", "SOURCE_CHECK_PASSED_RETRY_REQUIRED"}, projected.ToArray());
    }
    [TestMethod] public async Task UnknownPartBindingAndRepeatedPartialDiagnosticRemainUnattributed()
    {
        var source = Source("first1"); await Check(source, SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
        var unknown = new Dictionary<string, IReadOnlyList<string>> { ["other"] = new[] {"GOFILE_SHARE_NOT_FOUND"} };
        CollectionAssert.AreEqual(new[] {"GOFILE_SHARE_NOT_FOUND"}, (await Project(Plan(source), ["GOFILE_SHARE_NOT_FOUND"], unknown)).ToArray());
        unknown["part"] = new[] {"GOFILE_SHARE_NOT_FOUND"};
        CollectionAssert.AreEquivalent(new[] {"GOFILE_SHARE_NOT_FOUND", "SOURCE_CHECK_PASSED_RETRY_REQUIRED"},
            (await Project(Plan(source), ["GOFILE_SHARE_NOT_FOUND"], unknown)).ToArray());
    }
    [TestMethod] public async Task ObservationCommitInvalidatesNegativeAndPositiveCacheImmediately()
    {
        Assert.IsNull(await _store.ObservationAsync("key", default));
        var first = new SourceAvailabilityResult {Status = SourceAvailabilityStatuses.Available, ReasonCode = "SOURCE_METADATA_AVAILABLE", ProbeId = "test", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)};
        await _store.SaveObservationAsync("key", first, default);
        Assert.AreEqual(first, await _store.ObservationAsync("key", default));
        var next = first with {Status = SourceAvailabilityStatuses.ConfirmedUnavailable, CheckedAt = DateTimeOffset.UtcNow};
        await _store.SaveObservationAsync("key", next, default);
        Assert.AreEqual(next, await _store.ObservationAsync("key", default));
    }
    [TestMethod] public async Task ObservationEvictionAndConcurrentReadsKeepTheLastCommittedValue()
    {
        var first = new SourceAvailabilityResult {Status = SourceAvailabilityStatuses.Unknown,
            ReasonCode = "SOURCE_VALIDITY_UNKNOWN", ProbeId = "test", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)};
        await _store.SaveObservationAsync("key", first, default);
        await Task.WhenAll(Enumerable.Range(0, 4097).Select(i => _store.ObservationAsync("missing-" + i, default)));
        var next = first with {Status = SourceAvailabilityStatuses.ConfirmedUnavailable, ReasonCode = "SOURCE_SHARE_NOT_FOUND"};
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => _store.ObservationAsync("key", default))
            .Cast<Task>().Append(_store.SaveObservationAsync("key", next, default)));
        Assert.AreEqual(next, await _store.ObservationAsync("key", default));
    }
    private sealed class Probe : ISourceAvailabilityProbe
    {
        public int Calls;
        public SourceAvailabilityResult Result = null!;
        public bool CanHandle(CloudSource source) => true;
        public Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
        { Calls++; return Task.FromResult(Result); }
    }
}
