using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class CloudOutputMetadataTests
{
    [TestMethod]
    public void Explicit_output_has_paths_and_canonical_task_reference_but_no_execution_secrets()
    {
        var source = new CloudSource {Id = "source", Url = "https://example.invalid/?token=secret-url", ProviderId = "baidu",
            AccessCode = "private-access-code", ArchivePassword = "private-archive-password"};
        var workset = new DownloadWorkset {Targets = [new() {Id = "target", Sources = [source], SelectedSourceId = source.Id,
            DownloadTaskId = 42, DownloadArtifactId = "artifact", DownloadDirectory = "/downloads/source", Files = ["/downloads/source/file"],
            RemoteEntry = new("baidu", "account-ref", "/remote/saved"), Problem = "CLOUD_TOOL_FAILED: https://example.invalid/?token=error-secret"}]};
        var blocks = workset.GetWorkflowOutputMetadata("download.awaitCompletion");
        Assert.AreEqual("site:42", blocks.Single(b => b.Kind == "task").TaskId);
        Assert.AreEqual("/downloads/source", blocks.Single(b => b.Kind == "artifact").Directory);
        Assert.AreEqual("/remote/saved", blocks.Single(b => b.Kind == "remote").RemotePath);
        var json = JsonSerializer.Serialize(blocks);
        foreach (var secret in new[] {"private-access-code", "private-archive-password", "secret-url", "error-secret"})
            Assert.IsFalse(json.Contains(secret));
        Assert.AreEqual("CLOUD_TOOL_FAILED", blocks.Single(b => b.Kind == "warning").Code);
    }

    [TestMethod]
    public void Acquisition_summaries_only_publish_allowlisted_artifact_references()
    {
        var item = new AcquisitionWorkItem {ResourceId = 42, LeadKind = AcquisitionLeadKind.DirectUrl,
            LeadValue = "https://example.invalid/?token=secret", Variables = new Dictionary<string, string>
            { ["artifact.inputId"] = "input", ["artifact.planJson"] = "private-secret", ["artifact.password"] = "private-password" }};
        var json = JsonSerializer.Serialize(item.ToWorkflowOutputSummary());
        Assert.IsTrue(json.Contains("input"));
        Assert.IsFalse(json.Contains("private-secret"));
        Assert.IsFalse(json.Contains("private-password"));
    }
}
