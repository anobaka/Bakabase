using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Handoffs;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionSourceSelectionTests
{
    private static DownloadCandidateView Candidate(string id, string provider, bool ready = true,
        string? status = null, bool expired = false) => new(new() {Id = id, Url = $"https://example.test/{id}", ProviderId = provider},
        [], ready, ready ? "ready" : "unavailable", [], new() {Id = id, Url = $"https://example.test/{id}", ProviderId = provider},
        Availability: status == null ? null : new() {Status = status, ReasonCode = "TEST", ProbeId = "test",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(expired ? -1 : 5)});

    [TestMethod]
    public void KnownAvailableAndReadyOutrankProviderPriorityButExpiredObservationsDoNot()
    {
        var baidu = Candidate("baidu", "baidu");
        var http = Candidate("http", "http");
        Assert.AreEqual("baidu", AcquisitionSourceSelectionPolicy.Choose([http, baidu], ["baidu", "http"]).Candidate.Id);
        Assert.AreEqual("http", AcquisitionSourceSelectionPolicy.Choose([baidu with {CanSelect = false}, http], ["baidu"]).Candidate.Id);
        Assert.AreEqual("http", AcquisitionSourceSelectionPolicy.Choose([baidu,
            Candidate("http", "http", status: SourceAvailabilityStatuses.Available)], ["baidu"]).Candidate.Id);
        Assert.AreEqual("baidu", AcquisitionSourceSelectionPolicy.Choose([baidu,
            Candidate("http", "http", status: SourceAvailabilityStatuses.Available, expired: true)], ["baidu"]).Candidate.Id);
    }

    [DataTestMethod]
    [DataRow(SourceAvailabilityStatuses.AccessRequired)]
    [DataRow(SourceAvailabilityStatuses.TemporarilyUnavailable)]
    public void FreshAccessAndNetworkProblemsRankBehindUnknownMirrorsWithoutChangingDefaultForUnknown(string status)
    {
        var first = Candidate("baidu", "baidu", status: status);
        var unknown = Candidate("other", "http");
        Assert.AreEqual("other", AcquisitionSourceSelectionPolicy.Choose([first, unknown], ["baidu", "http"]).Candidate.Id);
        Assert.AreEqual("baidu", AcquisitionSourceSelectionPolicy.Choose([unknown,
            Candidate("baidu", "baidu", status: status, expired: true)], ["baidu", "http"]).Candidate.Id);
        Assert.AreEqual("baidu", AcquisitionSourceSelectionPolicy.Choose([unknown, Candidate("baidu", "baidu")], ["baidu", "http"]).Candidate.Id);
    }

    [TestMethod]
    public void RequiredGroupsRemainSeparateAndUnknownRelationshipsStillNeedReview()
    {
        var info = new Bakabase.Modules.PostParser.Models.Domain.PostDownloadInfo
        {Groups = [new() {Id = "main", Kind = "main"}, new() {Id = "part-two", Kind = "main"}]};
        var candidates = new[] {Candidate("0", "http") with {Candidate = new() {Id = "0", Url = "https://a.test/a", GroupId = "main"}},
            Candidate("1", "baidu") with {Candidate = new() {Id = "1", Url = "https://a.test/b", GroupId = "main"}},
            Candidate("2", "http") with {Candidate = new() {Id = "2", Url = "https://a.test/c", GroupId = "part-two"}}};
        var groups = PostAcquisitionSelection.Groups(info, candidates.Select(c => c.Candidate).ToArray());
        Assert.IsNotNull(groups);
        var chosen = PostAcquisitionSelection.Choose(groups, candidates, ["baidu"]);
        CollectionAssert.AreEqual(new[] {"1", "2"}, chosen.Select(c => c.Candidate.Id).ToArray());
        Assert.IsNull(PostAcquisitionSelection.Groups(info,
            candidates.Select(c => c.Candidate with {GroupId = null}).ToArray()));
    }

    private static AcquisitionPlan Plan(string mode = "automatic") => new()
    {
        Title = "Resource", SourceProviderPriority = ["baidu", "http"], Parts = [new()
        {
            Id = "main", SelectedSourceId = "primary", SelectionMode = mode,
            Processing = new() {Requirement = "notRequired"}, ProcessingSource = new("producer", "item", "1", "1", "primary"),
            Sources = [new() {Id = "primary", Url = "https://example.test/main.zip", Kind = "http", AccessCode = "first-access", ArchivePassword = "first-archive",
                Processing = new() {Requirement = "notRequired"}},
                new() {Id = "mirror", Url = "https://example.test/mirror.rar", Kind = "http", AccessCode = "second-access", ArchivePassword = "second-archive",
                    Processing = new() {Requirement = "required", Steps = [new() {Id = "unpack", Op = "extractArchive", Input = "download"}]},
                    ProcessingSource = new("producer", "item", "1", "1", "mirror"), AppliedProcessingRevisionId = "review-2"}]
        }]
    };

    [TestMethod]
    public void PreStartFallbackCopiesMirrorInstructionsAndKeepsOriginalEvidenceAndPasswords()
    {
        var plan = Plan(); var before = JsonSerializer.Serialize(plan);
        var updated = AcquisitionSourceSelectionPolicy.ApplyPreStartFallback(plan, [Candidate("primary", "baidu", false), Candidate("mirror", "http")]);
        Assert.AreEqual("mirror", updated.Parts.Single().SelectedSourceId);
        Assert.AreEqual("required", updated.Parts.Single().Processing!.Requirement);
        Assert.AreEqual("mirror", updated.Parts.Single().ProcessingSource!.ResultKey);
        Assert.AreEqual("review-2", updated.Parts.Single().AppliedProcessingRevisionId);
        Assert.AreEqual(2, updated.Parts.Single().Sources.Count);
        Assert.AreEqual("first-archive", updated.Parts.Single().Sources[0].ArchivePassword);
        Assert.AreEqual("second-archive", updated.Parts.Single().Sources[1].ArchivePassword);
        Assert.AreEqual(before, JsonSerializer.Serialize(plan));
        var execution = AcquisitionPlanCompiler.Workset(1, 2, updated, []);
        Assert.AreEqual("mirror", execution.Targets.Single().Sources.Single().Id,
            "A frozen run must not change mirrors after processing steps have been compiled.");
        Assert.AreEqual("second-access", execution.Targets.Single().SelectedSource!.AccessCode);
        Assert.AreEqual("second-archive", execution.Targets.Single().SelectedSource!.ArchivePassword);
    }

    [TestMethod]
    public void ManualLegacyAndReadySelectionsNeverChangeAndMissingAlternativesNeverDropParts()
    {
        var check = new[] {Candidate("primary", "baidu", false), Candidate("mirror", "http")};
        var manual = Plan("manual");
        Assert.AreSame(manual, AcquisitionSourceSelectionPolicy.ApplyPreStartFallback(manual, check));
        var legacy = JsonSerializer.Deserialize<AcquisitionPlan>("""
            {"Title":"Legacy","Parts":[{"Id":"main","SelectedSourceId":"primary","Sources":[{"Id":"primary","Url":"https://example.test/a"},{"Id":"mirror","Url":"https://example.test/b"}]}]}
            """)!;
        Assert.AreEqual("manual", legacy.Parts.Single().SelectionMode);
        Assert.AreSame(legacy, AcquisitionSourceSelectionPolicy.ApplyPreStartFallback(legacy, check));
        var automatic = Plan();
        Assert.AreSame(automatic, AcquisitionSourceSelectionPolicy.ApplyPreStartFallback(automatic,
            [Candidate("primary", "baidu"), Candidate("mirror", "http", status: SourceAvailabilityStatuses.Available)]));
        Assert.AreSame(automatic, AcquisitionSourceSelectionPolicy.ApplyPreStartFallback(automatic,
            [Candidate("primary", "baidu", false), Candidate("mirror", "http", false)]));
    }

    [TestMethod]
    public void MalformedMirrorInstructionsRemainEvidenceAndDoNotBlockGoodSelectedSource()
    {
        const string invalid = "{\"requirement\":\"required\",\"steps\":[{\"id\":\"step\",\"op\":\"notAnOperation\"}]}";
        var first = new DownloadCandidate {Id = "0", Url = "https://example.test/a.zip", GroupId = "main", MirrorGroupConfirmed = true,
            ExtractionPlanJson = "{\"requirement\":\"notRequired\",\"steps\":[]}"};
        var invalidMirror = first with {Id = "1", Url = "https://example.test/b.rar", ExtractionPlanJson = invalid};
        var checkedFirst = new DownloadCandidateView(first, [], true, "ready", [],
            new() {Id = "0", Url = first.Url, Kind = "http", ProviderId = "http"});
        var saved = (new DownloadIntentRequest {Title = "Saved", Candidates = [first, invalidMirror]}, (string?)null,
            (IReadOnlyDictionary<string, Bakabase.Service.Components.Acquisition.ProcessingPlans.SourceProcessingPlanView>)
                new Dictionary<string, Bakabase.Service.Components.Acquisition.ProcessingPlans.SourceProcessingPlanView>(),
            new Bakabase.Modules.PostParser.Models.Domain.PostDownloadMediaView());
        var plan = PostAcquisitionPlanAdapter.BuildPlan(new() {TaskId = 1, Revision = 2, ResourceIndices = [0]}, saved,
            [checkedFirst], "/downloads", automaticSelection: true);
        var mirror = plan.Parts.Single().Sources[1];
        Assert.AreEqual(invalid, mirror.OriginalProcessingPlanJson);
        Assert.AreEqual("SOURCE_PROCESSING_INVALID", mirror.ProcessingIssueCode);
        Assert.IsNull(mirror.Processing);
        AcquisitionPlanCompiler.Compile(plan);
        Assert.AreSame(plan, AcquisitionSourceSelectionPolicy.ApplyPreStartFallback(plan,
            [Candidate("0", "http", false), Candidate("1", "http")]));
        Assert.ThrowsExactly<ArgumentException>(() => AcquisitionPlanCompiler.Compile(plan with
            {Parts = [plan.Parts.Single() with {SelectedSourceId = "1"}]}));
    }

    [TestMethod]
    public void UnsupportedMirrorIsPreservedButCannotBreakFrozenSelectedExecution()
    {
        var plan = Plan("manual");
        plan = plan with {Parts = [plan.Parts.Single() with {Sources = [plan.Parts.Single().Sources[0],
            new() {Id = "bad-mirror", Url = "javascript:untrusted", ProviderId = "unknown", Kind = "auto"}]}]};
        AcquisitionPlanCompiler.Compile(plan);
        Assert.AreEqual("primary", AcquisitionPlanCompiler.Workset(1, 1, plan, []).Targets.Single().Sources.Single().Id);
        Assert.AreEqual("javascript:untrusted", plan.Parts.Single().Sources[1].Url);
    }
}
