using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Extensions;
using Bakabase.Modules.Workflow.Services;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowOutputMetadataTests
{
    private sealed record SensitiveItem : IWorkflowOutputMetadata
    {
        public string Password => throw new InvalidOperationException("Execution getters must not be serialized");
        public IReadOnlyList<WorkflowOutputBlock> GetWorkflowOutputMetadata(string activityKind) =>
            [new() {Kind = "scalar", Key = "count", Value = "3"}];
    }

    [TestMethod]
    public void ExplicitProjectionDoesNotInspectExecutionPayloadAndUsesAllowlistedBlockFields()
    {
        var capture = new WorkflowOutputMetadataCapture();
        capture.AddItem(new SensitiveItem(), "test");
        capture.AddItem(new {Password = "secret-canary"}, "test");
        capture.Add([new() {Kind = "remote", Key = "source", Url = "https://example.com/share?token=secret#password",
            TaskId = "must-not-cross-kind", Value = "secret-canary"}]);
        Assert.AreEqual(2, capture.Outputs.Count);
        Assert.AreEqual("https://example.com/share", capture.Outputs[1].Url);
        Assert.IsNull(capture.Outputs[1].TaskId);
        Assert.IsFalse(capture.Json.Contains("secret"));
        Assert.IsFalse(capture.Json.Contains("must-not-cross-kind"));
    }

    [TestMethod]
    public void PostParserInspectionContainsOnlyTaskReferenceAndCounts()
    {
        var input = new PostParserInput {TaskId = 7, Revision = 3, Text = "private-post-canary", Link = "https://private.example/post"};
        var result = new PostParserResultItem(input, new()
        {
            Title = "private-title-canary", Groups = [new() {Id = "one", Evidence = ["private-evidence-canary"]}],
            Resources = [
                new() {Link = "https://private.example/share", Code = "private-code-canary", Password = "private-password-canary",
                    Extraction = new() {Requirement = "required", Evidence = ["private-plan-canary"]}},
                new() {Extraction = new() {Requirement = "notRequired"}}, new()]
        });
        var capture = new WorkflowOutputMetadataCapture();
        capture.AddItem(result, "postParser.extractDownloadInfo");
        Assert.AreEqual("7", capture.Outputs.Single(b => b.Kind == "task").TaskId);
        Assert.AreEqual("1", capture.Outputs.Single(b => b.Key == "postParser.processingRequired").Value);
        Assert.AreEqual("1", capture.Outputs.Single(b => b.Key == "postParser.processingUnknown").Value);
        Assert.AreEqual("1", capture.Outputs.Single(b => b.Key == "postParser.processingNotRequired").Value);
        var summary = WorkflowOutputPreview.Capture([input, new PostParserContentItem(input,
            new() {MainHtml = "private-html-canary"}), result]);
        Assert.IsFalse(summary.Truncated);
        Assert.IsFalse((capture.Json + summary.Json).Contains("private-"));
        Assert.IsFalse((capture.Json + summary.Json).Contains("private.example"));
    }

    [TestMethod]
    public void MetadataIsBoundedAndNewObservationOfSameOutputReplacesOnlyThatOutput()
    {
        var capture = new WorkflowOutputMetadataCapture();
        capture.Add([new() {Kind = "scalar", Key = "count", Value = "1"}]);
        capture.Add([new() {Kind = "scalar", Key = "count", Value = "2"}]);
        Assert.AreEqual(1, capture.Outputs.Count);
        Assert.AreEqual("2", capture.Outputs[0].Value);
        capture.Add(Enumerable.Range(0, 1000).Select(i => new WorkflowOutputBlock
            {Kind = "scalar", Key = i.ToString(), Value = new string('x', 3000)}));
        Assert.IsTrue(capture.Truncated);
        Assert.IsTrue(capture.Outputs.Count <= WorkflowOutputMetadataCapture.MaximumBlocks);
        Assert.IsTrue(capture.Json.Length <= WorkflowOutputMetadataCapture.MaximumCharacters);
    }

    [TestMethod]
    public void LegacyInputHistoryIsUnknownAndExplicitEmptyInputHistoryRemainsRecorded()
    {
        var node = new WorkflowNodeExecutionDbModel();
        Assert.IsFalse(node.ToDomainModel().InputsRecorded);
        node.InputsJson = "[]";
        Assert.IsTrue(node.ToDomainModel().InputsRecorded);
        Assert.AreEqual(0, node.ToDomainModel().Inputs.Count);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ArtifactManifestPaginationRequiresExactRunReferenceAndNeverReadsFilesystem(bool inputReference)
    {
        var sp = await TestServiceBuilder.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var artifactId = Guid.NewGuid().ToString("N");
        var opId = Guid.NewGuid().ToString("N");
        var run = new WorkflowRunDbModel {StartedAt = DateTime.Now};
        run.NodeExecutions.Add(new() {NodeId = "activity:1", Kind = "test", Attempt = 1, StartedAt = DateTime.UtcNow,
            Status = "succeeded", OutputsJson = JsonSerializer.Serialize(new[] {new WorkflowOutputBlock
                {Kind = "artifact", Key = "result", ArtifactId = artifactId, Directory = "/historical/old-location"}}, WorkflowJson.Options)});
        var other = new WorkflowRunDbModel {StartedAt = DateTime.Now};
        if (inputReference)
        {
            run.NodeExecutions.Single().InputsJson = run.NodeExecutions.Single().OutputsJson;
            run.NodeExecutions.Single().OutputsJson = "[]";
        }
        ArtifactManifestEntry[] entries = [new("folder/inner/a.txt", 1, new string('a', 64)),
            new("folder/b.txt", 2, new string('b', 64)), new("c.txt", 3, new string('c', 64))];
        var fingerprint = ArtifactManifestFiles.Fingerprint(entries);
        db.WorkflowRuns.AddRange(run, other);
        db.ArtifactOperations.Add(new() {Id = opId, IntentKey = opId, Kind = "test"});
        db.Artifacts.Add(new() {Id = artifactId, OperationId = opId, RootPath = "/nonexistent/do-not-scan", Fingerprint = fingerprint,
            ManifestJson = JsonSerializer.Serialize(new ArtifactManifest(entries, fingerprint), WorkflowJson.Options)});
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<WorkflowOutputQueryService<BakabaseDbContext>>();
        var page = await service.GetArtifactFilesAsync(run.Id, artifactId, 2, 2);
        Assert.IsNotNull(page);
        Assert.AreEqual(3, page.FileCount);
        Assert.AreEqual(6L, page.TotalBytes);
        Assert.AreEqual("c.txt", page.Entries.Single().RelativePath);
        Assert.AreEqual("/nonexistent/do-not-scan", page.Directory);
        var root = await service.GetArtifactDirectoryAsync(run.Id, artifactId, null, 1, 1, default);
        Assert.AreEqual(2, root!.ChildCount);
        Assert.AreEqual("folder", root.Entries.Single().RelativePath);
        Assert.IsTrue(root.Entries.Single().IsDirectory);
        var branch = await service.GetArtifactDirectoryAsync(run.Id, artifactId, "folder", 2, 1, default);
        Assert.AreEqual("folder/b.txt", branch!.Entries.Single().RelativePath);
        Assert.AreEqual(2L, branch.Entries.Single().Length);
        Assert.IsNull(await service.GetArtifactDirectoryAsync(other.Id, artifactId, "folder", 1, 1, default));
        Assert.IsNull(await service.GetArtifactDirectoryAsync(run.Id, artifactId, "../folder", 1, 1, default));
        Assert.IsNull(await service.GetArtifactDirectoryAsync(run.Id, artifactId, "unregistered", 1, 1, default));
        Assert.AreEqual(ArtifactLocationState.Available, db.Artifacts.Single(a => a.Id == artifactId).State);
        var recordedOutput = run.NodeExecutions.Single().OutputsJson;
        db.Artifacts.Single(a => a.Id == artifactId).RootPath = "/nonexistent/relocated";
        await db.SaveChangesAsync();
        Assert.AreEqual("/nonexistent/relocated", (await service.GetArtifactFilesAsync(run.Id, artifactId))!.Directory);
        Assert.AreEqual(recordedOutput, run.NodeExecutions.Single().OutputsJson);
        db.Artifacts.Single(a => a.Id == artifactId).State = ArtifactLocationState.Retired;
        await db.SaveChangesAsync();
        var retired = await service.GetArtifactFilesAsync(run.Id, artifactId);
        Assert.AreEqual("retired", retired!.State);
        CollectionAssert.AreEqual(entries, retired.Entries.ToArray());
        Assert.AreEqual(recordedOutput, run.NodeExecutions.Single().OutputsJson);
        Assert.IsNull(await service.GetArtifactFilesAsync(other.Id, artifactId));
        Assert.IsNull(await service.GetArtifactFilesAsync(run.Id, "unowned"));
        foreach (var invalidManifest in new[]
                 {
                     "{\"entries\":null,\"fingerprint\":\"fp\"}",
                     "{\"entries\":[null],\"fingerprint\":\"fp\"}",
                     "{\"entries\":[{\"relativePath\":\"a.txt\",\"length\":1,\"sha256\":null}],\"fingerprint\":\"fp\"}"
                 })
        {
            var row = db.Artifacts.Single(a => a.Id == artifactId);
            row.ManifestJson = invalidManifest;
            row.Fingerprint = "fp";
            await db.SaveChangesAsync();
            Assert.IsNull(await service.GetArtifactFilesAsync(run.Id, artifactId));
        }
    }
}
