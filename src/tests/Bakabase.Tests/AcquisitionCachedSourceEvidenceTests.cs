using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.Acquisition.ProcessingPlans;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionCachedSourceEvidenceTests
{
    private const string Selected = "https://download.example/selected";
    private const string Other = "https://download.example/other";
    private const string Password = "literal@publisher";
    private BakabaseDbContext db = null!;
    private PostParserTaskDbModel post = null!;
    private AcquisitionTaskDbModel task = null!;
    private static WorkflowSourceRef Source => new("postParser", "task", "6", "3", "0");
    private static string Json(object value) => JsonSerializer.Serialize(value, WorkflowJson.Options);
    private static AcquisitionPlan Plan => new() {Title = "Fixture", Parts = [new()
    {
        Id = "part", SelectedSourceId = "selected", ProcessingSource = Source,
        Sources = [new() {Id = "selected", Url = Selected, AccessCode = "Code123"}]
    }]};
    private static PostDownloadInfo Info => new() {IsComplete = true, Title = "Fixture", Resources =
        [new() {Link = Selected}, new() {Link = Other}]};
    private static string Html => $"<p><a href='{Selected}?pwd=Code123'>下载</a> 提取码：Code123</p>" +
        $"<div>密码↓</div><h6>此帖售价0SP币 已419人购买</h6><blockquote>{Password}</blockquote>";

    [TestInitialize]
    public async Task Initialize()
    {
        db = new(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync();
        db.WorkflowRuns.Add(new() {Id = 40, WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Running});
        post = new() {Id = 6, Revision = 3, ParsingState = "complete", Link = "https://forum.example/6",
            ContentSnapshotJson = Json(new PostContent {MainHtml = Html}),
            Results = Json(new Dictionary<string, PostDownloadInfo> {{"DownloadInfo", Info}})};
        task = new() {Id = 9, WorkflowRunId = 40, PlanJson = Json(Plan), SourceRefsJson = Json(new[] {Source})};
        db.PostParserTasks.Add(post); db.AcquisitionTasks.Add(task); await db.SaveChangesAsync();
    }

    [TestCleanup] public async Task Cleanup() => await db.DisposeAsync();
    private Task<AcquisitionCachedSourceEvidence.Evidence?> Read() =>
        AcquisitionCachedSourceEvidence.ReadAsync(db, null, 40, "part", Selected, default);

    [TestMethod]
    public async Task Cached_unlocked_literal_reaches_AI_even_when_old_parse_omitted_password()
    {
        var originalPlan = task.PlanJson; var originalPost = post.ContentSnapshotJson;
        var evidence = await Read(); Assert.IsNotNull(evidence);
        CollectionAssert.AreEqual(new[] {Password}, evidence.Passwords.Select(p => p.Value).ToArray());
        Assert.IsTrue(evidence.Passwords[0].Labelled);
        var context = await new AcquisitionFileProcessingEvidence(db).ReadAsync(40, "part", Selected, default);
        Assert.IsNotNull(context); StringAssert.Contains(context, Password);
        StringAssert.Contains(context, "Untrusted source data");
        Assert.IsFalse(context.Contains("Code123")); Assert.IsFalse(context.Contains(Selected));
        Assert.IsTrue(context.Length <= AcquisitionFileProcessingEvidence.MaximumEvidenceChars);
        Assert.AreEqual(originalPlan, (await db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanJson);
        Assert.AreEqual(originalPost, (await db.PostParserTasks.AsNoTracking().SingleAsync()).ContentSnapshotJson);
        Assert.AreEqual(1, await db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task Projection_changes_do_not_change_saved_source_fingerprint()
    {
        var snapshot = await new PostProcessingPlanSourceReader(db).ReadAsync(Source, default);
        var resource = Info.Resources[0];
        Assert.AreEqual(Json(new {resource, post.ContentSnapshotJson, post.Text}), snapshot.SourceFingerprint);
        StringAssert.Contains(snapshot.EvidenceTexts.Single(), Selected);
        StringAssert.Contains(snapshot.EvidenceTexts.Single(), Password);
    }

    [DataTestMethod]
    [DataRow("revision")]
    [DataRow("deleted")]
    [DataRow("index")]
    [DataRow("wrong-source")]
    [DataRow("ambiguous-origin")]
    [DataRow("unbound-origin")]
    public async Task Unmatched_or_unstable_saved_source_cannot_supply_password(string change)
    {
        var plan = Plan;
        switch (change)
        {
            case "revision": post.Revision = 4; break;
            case "deleted": post.IsDeleted = true; break;
            case "index": plan.Parts[0] = plan.Parts[0] with {ProcessingSource = Source with {ResultKey = "8"}}; task.PlanJson = Json(plan); break;
            case "wrong-source": plan.Parts[0] = plan.Parts[0] with {ProcessingSource = Source with {ResultKey = "1"}}; task.PlanJson = Json(plan); break;
            case "ambiguous-origin": plan.Parts[0] = plan.Parts[0] with {ProcessingSource = null}; task.PlanJson = Json(plan);
                task.SourceRefsJson = Json(new[] {Source, Source with {ResultKey = "1"}}); break;
            case "unbound-origin": task.SourceRefsJson = "[]"; break;
        }
        await db.SaveChangesAsync(); Assert.IsNull(await Read());
    }

    [TestMethod]
    public async Task Other_resource_and_unlinked_comments_do_not_contribute_passwords_or_excerpts()
    {
        post.ContentSnapshotJson = Json(new PostContent {MainHtml = Html +
            $"<p><a href='{Other}'>其他资源</a></p><p>密码↓</p><blockquote>OtherSecret123</blockquote>",
            Comments = [new() {Html = "<p>密码↓</p><blockquote>CommentSecret123</blockquote>"}]});
        await db.SaveChangesAsync(); var evidence = await Read(); Assert.IsNotNull(evidence);
        CollectionAssert.AreEqual(new[] {Password}, evidence.Passwords.Select(p => p.Value).ToArray());
        var context = string.Join("\n", evidence.Excerpts);
        Assert.IsFalse(context.Contains("OtherSecret123")); Assert.IsFalse(context.Contains("CommentSecret123"));
    }

    [DataTestMethod]
    [DataRow("a&amp;b", "a&b")]
    [DataRow("a&amp;amp;b", "a&amp;b")]
    [DataRow("&#108;&#105;teral&#64;publisher", Password)]
    [DataRow("&quot;two words&quot;", "two words")]
    public void Visible_password_block_decodes_entities_once_without_requiring_digits(string html, string expected)
    {
        var candidates = AcquisitionArchivePasswordEvidence.Inspect(
            $"<a href='{Selected}'>download</a><div>密码↓</div><blockquote>{html}</blockquote>", [Selected], new HashSet<string>(), false);
        CollectionAssert.AreEqual(new[] {expected}, candidates.Select(c => c.Value).ToArray());
    }

    [TestMethod]
    public void Hidden_markup_and_access_codes_are_not_password_evidence()
    {
        var html = $"<a href='{Selected}'>download</a><div>提取码：Code123</div>" +
            "<div>密码↓</div><blockquote hidden>Hidden123</blockquote><script>Script123</script>" +
            "<div aria-hidden='true'>Aria123</div><div style='display:none'>Styled123</div>" +
            "<template>Template123</template><iframe>Frame123</iframe><img alt='Image123'>" +
            "<blockquote>Code123</blockquote>";
        Assert.AreEqual(0, AcquisitionArchivePasswordEvidence.Inspect(html, [Selected], new HashSet<string>(), false).Count);
        var text = ProcessingPlanSourceText.Visible(html);
        foreach (var value in new[] {"Hidden123", "Script123", "Aria123", "Styled123", "Template123", "Frame123", "Image123"})
            Assert.IsFalse(text.Contains(value), value);
    }

    [DataTestMethod]
    [DataRow("楼主论坛名字")]
    [DataRow("作者名称")]
    [DataRow("见网盘内说明")]
    [DataRow("publisher name")]
    public void Unresolved_author_or_location_references_are_not_invented_passwords(string reference)
    {
        Assert.AreEqual(0, AcquisitionArchivePasswordEvidence.Inspect(
            $"<a href='{Selected}'>download</a><p>密码↓</p><blockquote>{reference}</blockquote>",
            [Selected], new HashSet<string>(), false).Count);
    }

    [TestMethod]
    public void Original_technical_evidence_survives_bounded_old_summary_context()
    {
        var original = new AcquisitionCachedSourceEvidence.Evidence(Source, "stable-hash",
            ["解压密码：" + Password, new string('文', 3100)], []);
        var info = Info with {Availability = new() {Evidence = Enumerable.Repeat(new string('文', 1000), 8).ToList()}};
        var group = new PostDownloadGroup {Title = new string('文', 1000), Summary = new string('文', 1000),
            Evidence = Enumerable.Repeat(new string('文', 1000), 6).ToList()};
        var result = AcquisitionFileProcessingEvidence.Describe(info, null, group, 3, original);
        Assert.IsNotNull(result); Assert.IsTrue(result.Length <= AcquisitionFileProcessingEvidence.MaximumEvidenceChars);
        StringAssert.Contains(result, Password);
    }
}
