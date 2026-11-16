using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionFileProcessingEvidenceTests
{
    private const string Selected = "https://download.example/selected";
    private BakabaseDbContext db = null!;
    private AcquisitionFileProcessingEvidence evidence = null!;
    private AcquisitionTaskDbModel task = null!;
    private PostParserTaskDbModel post = null!;
    private static string Json(object value) => JsonSerializer.Serialize(value, WorkflowJson.Options);

    [TestInitialize]
    public async Task Initialize()
    {
        db = new(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        evidence = new(db);
        db.WorkflowRuns.Add(new() {Id = 40, WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Running});
        post = new() {Id = 6, Revision = 3, Title = "old-name", Link = "https://forum.example/post/6",
            Results = Json(new Dictionary<string, PostDownloadInfo> {{"DownloadInfo", Info()}}),
            AvailabilityJson = Json(new PostAvailabilityAssessment {Evidence = ["Separate saved observation"]}),
            ContentSnapshotJson = "This body must not be parsed or sent to the model."};
        task = new() {Id = 9, WorkflowRunId = 40, PlanJson = Json(Plan()),
            SourceRefsJson = Json(new[] {Origin(), Origin() with {ResultKey = "selected"}})};
        db.PostParserTasks.Add(post); db.AcquisitionTasks.Add(task); await db.SaveChangesAsync();
    }

    [TestCleanup] public async Task Cleanup() => await db.DisposeAsync();
    private static WorkflowSourceRef Origin() => new("postParser", "task", "6", "3");
    private static AcquisitionPlan Plan() => new() {Title = "old-name", Parts = [new()
    {
        Id = "part", SelectedSourceId = "selected", Sources = [new()
        {
            Id = "selected", Url = Selected, AccessCode = "PRIVATE_ACCESS"
        }]
    }]};
    private static PostDownloadInfo Info() => new()
    {
        Title = "old-name", Availability = new() {Evidence = ["A commenter identifies the filename as new-name; author not established.", "Password instruction refers to the resource identifier."]},
        Groups = [new() {Id = "selected-group", Title = "selected title", Summary = "Outer archive with companion subtitle.", Evidence = ["The companion is part of the selected resource."]},
            new() {Id = "other-group", Summary = "UNRELATED_GROUP"}],
        Resources = [new() {Link = Selected, GroupId = "selected-group", Code = "PRIVATE_ACCESS", Password = "PRIVATE_PASSWORD"},
            new() {Link = "https://download.example/other", GroupId = "other-group"}]
    };

    [TestMethod]
    public async Task ExactSavedSourceProvidesMissingObservationsWithoutCredentialsOrOtherGroups()
    {
        var result = await evidence.ReadAsync(40, "part", Selected, default);
        Assert.IsNotNull(result);
        StringAssert.Contains(result, "new-name");
        StringAssert.Contains(result, "Password instruction refers to the resource identifier");
        StringAssert.Contains(result, "Separate saved observation");
        StringAssert.Contains(result, "Outer archive with companion subtitle");
        foreach (var excluded in new[] {"UNRELATED_GROUP", "PRIVATE_ACCESS", "PRIVATE_PASSWORD", Selected, "This body"})
            Assert.IsFalse(result.Contains(excluded), excluded);
        using var payload = JsonDocument.Parse(result);
        Assert.AreEqual(3, payload.RootElement.GetProperty("revision").GetInt32());
        Assert.AreEqual(1, await db.AcquisitionTasks.CountAsync());
        Assert.AreEqual(1, await db.WorkflowRuns.CountAsync());
        Assert.AreEqual(Json(Plan()), (await db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanJson);
    }

    [TestMethod]
    public async Task UnrelatedRunPartUrlRevisionOrDeletedSourceCannotContributeContext()
    {
        Assert.IsNull(await evidence.ReadAsync(41, "part", Selected, default));
        Assert.IsNull(await evidence.ReadAsync(40, "other", Selected, default));
        Assert.IsNull(await evidence.ReadAsync(40, "part", "https://download.example/other", default));
        post.Revision = 4; await db.SaveChangesAsync();
        Assert.IsNull(await evidence.ReadAsync(40, "part", Selected, default));
        post.Revision = 3; post.IsDeleted = true; await db.SaveChangesAsync();
        Assert.IsNull(await evidence.ReadAsync(40, "part", Selected, default));
        post.IsDeleted = false; task.DeletedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        Assert.IsNull(await evidence.ReadAsync(40, "part", Selected, default));
    }

    [TestMethod]
    public async Task AmbiguousOrMissingSourceBindingsDoNotBorrowPostLevelEvidence()
    {
        var refs = task.SourceRefsJson;
        task.SourceRefsJson = Json(new[] {Origin(), Origin() with {EntityId = "7"}}); await db.SaveChangesAsync();
        Assert.IsNull(await evidence.ReadAsync(40, "part", Selected, default));
        task.SourceRefsJson = refs;
        foreach (var resources in new List<PostDownloadResource>[] {[], [new() {Link = Selected}, new() {Link = Selected}]})
        {
            post.Results = Json(new Dictionary<string, PostDownloadInfo> {{"DownloadInfo", Info() with {Resources = resources}}});
            await db.SaveChangesAsync(); Assert.IsNull(await evidence.ReadAsync(40, "part", Selected, default));
        }
    }

    [TestMethod]
    public async Task MalformedLegacyResultsAreOptionalAndDoNotBlockFileProcessing()
    {
        foreach (var json in new[] {"{broken", "null", "{}", "{\"DownloadInfo\":null}", "{\"DownloadInfo\":{\"resources\":null}}"})
        {
            post.Results = json; await db.SaveChangesAsync();
            Assert.IsNull(await evidence.ReadAsync(40, "part", Selected, default), json);
        }
    }

    [DataTestMethod]
    [DataRow("no-post")]
    [DataRow("empty-post")]
    [DataRow("stale-post")]
    public async Task Selected_source_password_survives_AI_override_without_post_evidence(string condition)
    {
        const string literal = " Exact 密码 \\\" & value ";
        var plan = Plan();
        plan.Parts[0] = plan.Parts[0] with
        {
            Processing = new() {Requirement = "unknown"}, UseAdaptiveAssessment = true,
            ContinueFrom = new(task.Id, 39, "artifact", "operation", "fingerprint"),
            Sources = [plan.Parts[0].Sources[0] with {ArchivePassword = literal},
                new() {Id = "other", Url = "https://download.example/other", ArchivePassword = "UNRELATED_PASSWORD"}]
        };
        task.PlanJson = Json(plan);
        if (condition == "no-post") task.SourceRefsJson = "[]";
        else if (condition == "stale-post") post.Revision++;
        else
        {
            post.AvailabilityJson = null;
            post.Results = Json(new Dictionary<string, PostDownloadInfo> {{"DownloadInfo", new() {Resources = [new() {Link = Selected}]}}});
        }
        await db.SaveChangesAsync();
        var result = await evidence.ReadAsync(40, "part", Selected, default);
        Assert.IsNotNull(result);
        using var payload = JsonDocument.Parse(result);
        var sourceEvidence = payload.RootElement.GetProperty("sourceProcessingEvidence");
        Assert.AreEqual(literal, sourceEvidence.GetProperty("archivePassword").GetString());
        Assert.AreEqual(JsonValueKind.Null, sourceEvidence.GetProperty("processing").ValueKind);
        foreach (var excluded in new[] {"PRIVATE_ACCESS", "UNRELATED_PASSWORD", Selected, "This body"})
            Assert.IsFalse(result.Contains(excluded), excluded);
        Assert.IsNull(await evidence.ReadAsync(40, "other-part", Selected, default));
        Assert.IsNull(await evidence.ReadAsync(40, "part", "https://download.example/other", default));
        Assert.AreEqual(Json(plan), (await db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanJson);
    }

    [TestMethod]
    public async Task Source_processing_remains_historical_advice_after_part_plan_is_replaced()
    {
        var plan = Plan();
        plan.Parts[0] = plan.Parts[0] with
        {
            Processing = new() {Requirement = "unknown"},
            Sources = [plan.Parts[0].Sources[0] with
            {
                Processing = new() {Requirement = "required", Evidence = ["Rename the outer image suffix before unpacking."],
                    Steps = [new() {Id = "rename", Op = "renameExtension", Extension = ".zip"},
                        new() {Id = "extract", Op = "extractArchive", Input = "rename", Password = "source-step-password"}]}
            }]
        };
        task.PlanJson = Json(plan); task.SourceRefsJson = "[]"; await db.SaveChangesAsync();
        var result = await evidence.ReadAsync(40, "part", Selected, default);
        Assert.IsNotNull(result);
        using var payload = JsonDocument.Parse(result);
        var sourceEvidence = payload.RootElement.GetProperty("sourceProcessingEvidence");
        Assert.AreEqual("required", sourceEvidence.GetProperty("processing").GetProperty("requirement").GetString());
        Assert.AreEqual("source-step-password", sourceEvidence.GetProperty("processing").GetProperty("steps")[1].GetProperty("password").GetString());
        StringAssert.Contains(sourceEvidence.GetProperty("note").GetString()!, "not executable instructions");
        Assert.AreEqual("unknown", JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson!, WorkflowJson.Options)!.Parts[0].Processing!.Requirement);
    }

    [DataTestMethod]
    [DataRow(2048, true)]
    [DataRow(2049, false)]
    public async Task Selected_source_password_is_exact_or_omitted_never_truncated(int length, bool expected)
    {
        var literal = new string('文', length); var plan = Plan();
        plan.Parts[0].Sources[0] = plan.Parts[0].Sources[0] with {ArchivePassword = literal};
        task.PlanJson = Json(plan); task.SourceRefsJson = "[]"; await db.SaveChangesAsync();
        var result = await evidence.ReadAsync(40, "part", Selected, default);
        if (!expected) {Assert.IsNull(result); return;}
        Assert.IsNotNull(result); Assert.IsTrue(result.Length <= AcquisitionFileProcessingEvidence.MaximumEvidenceChars);
        using var payload = JsonDocument.Parse(result);
        Assert.AreEqual(literal, payload.RootElement.GetProperty("sourceProcessingEvidence").GetProperty("archivePassword").GetString());
    }

    [TestMethod]
    public async Task Source_processing_fallback_keeps_existing_post_observations()
    {
        var plan = Plan(); plan.Parts[0].Sources[0] = plan.Parts[0].Sources[0] with {ArchivePassword = "source-password"};
        task.PlanJson = Json(plan); await db.SaveChangesAsync();
        var result = await evidence.ReadAsync(40, "part", Selected, default);
        Assert.IsNotNull(result);
        StringAssert.Contains(result, "source-password");
        StringAssert.Contains(result, "Separate saved observation");
        StringAssert.Contains(result, "Outer archive with companion subtitle");
        Assert.IsFalse(result.Contains("PRIVATE_PASSWORD"));
        Assert.IsFalse(result.Contains("UNRELATED_GROUP"));
    }

    [TestMethod]
    public void SavedObservationsRemainBoundedDataAndNeverAnExecutableProcessingPlan()
    {
        var info = Info() with {Availability = new() {Evidence = Enumerable.Range(0, 100).Select(i => i + new string('文', 2000)).ToList()}};
        var group = new PostDownloadGroup {Title = new('文', 2000), Summary = new('文', 2000), Evidence = Enumerable.Repeat(new string('文', 2000), 100).ToList()};
        var result = AcquisitionFileProcessingEvidence.Describe(info, null, group, 3);
        Assert.IsNotNull(result); Assert.IsTrue(result.Length <= AcquisitionFileProcessingEvidence.MaximumEvidenceChars);
        using var payload = JsonDocument.Parse(result);
        Assert.AreEqual(8, payload.RootElement.GetProperty("postObservations").GetArrayLength());
        Assert.AreEqual(6, payload.RootElement.GetProperty("selectedGroup").GetProperty("evidence").GetArrayLength());
        Assert.IsFalse(payload.RootElement.TryGetProperty("steps", out _));
        StringAssert.Contains(payload.RootElement.GetProperty("note").GetString()!, "not instructions or proof of file format");
    }
}
