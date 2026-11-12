using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class PostAcquisitionPlanTests
{
    [DataTestMethod]
    [DataRow("confirmed", 1)]
    [DataRow("ungrouped", 0)]
    [DataRow("supplement", 0)]
    [DataRow("differentSource", 0)]
    public async Task SourceRevision_post_boundary_only_offers_same_group_mirrors_and_keeps_source_local_processing(string kind, int count)
    {
        var post = await Saved();
        var result = JsonNode.Parse(post.Results!)!;
        if (kind == "ungrouped") result["DownloadInfo"]!["resources"]![1]!["groupId"] = null;
        if (kind == "supplement") result["DownloadInfo"]!["resources"]![1]!["groupId"] = "preview";
        post.Results = result.ToJsonString(); await Db.SaveChangesAsync();
        var plan = new AcquisitionPlan {Title = post.Title!, Parts = [new() {Id = "part", SelectedSourceId = "0",
            Sources = [new() {Id = "0", Url = kind == "differentSource" ? "https://example.org/unrelated.zip" : "https://example.test/a.zip",
                ArchivePassword = "saved-password"}]}]};
        var before = JsonSerializer.Serialize(await Db.PostParserTasks.AsNoTracking().SingleAsync());
        var adapter = new PostSourceRevisionCandidates(_services.GetRequiredService<PostDownloadIntentAdapter>());
        var rows = await adapter.ReadAsync(plan, [new("postParser", "task", post.Id.ToString(), "4")], default);
        Assert.AreEqual(count, rows.Count);
        if (count != 0)
        {
            var row = rows.Single(); Assert.AreEqual("1", row.Source.Id); Assert.IsNull(row.Source.ArchivePassword);
            Assert.AreEqual("unknown", row.Source.Processing!.Requirement);
            Assert.AreEqual("1", row.Source.ProcessingSource!.ResultKey);
            Assert.IsTrue(row.SourceRefs.Any(r => r.EntityId == post.Id.ToString() && r.Revision == "4"));
        }
        Assert.AreEqual(before, JsonSerializer.Serialize(await Db.PostParserTasks.AsNoTracking().SingleAsync()));
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => adapter.ReadAsync(plan,
            [new("postParser", "task", post.Id.ToString(), "3")], default));
    }
}
