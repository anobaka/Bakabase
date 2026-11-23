using System.Security.Cryptography;
using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Confirmed_processing_checks_private_snapshot_against_registered_source(bool replaceBeforeSnapshot)
    {
        var (_, registered, workset, context) = await ConnectedManualProcessingInput();
        var activity = new AcquisitionPlanProcessingActivity();
        var wait = await activity.ProcessItemAsync(context(null), workset, default);
        Assert.IsNotNull(wait.Suspension);
        var source = registered.Files.Single();
        var original = await File.ReadAllBytesAsync(source);
        var modified = original.ToArray();
        modified[0] ^= 1;
        var resumed = context(wait.Suspension.PromptJson);
        var preparing = false;
        var checkedContext = new WorkflowExecutionContext
        {
            RunId = resumed.RunId, WorkflowDefinitionId = resumed.WorkflowDefinitionId, TriggerKind = resumed.TriggerKind,
            Payload = resumed.Payload, ActivityConfigJson = resumed.ActivityConfigJson, ResumePromptJson = resumed.ResumePromptJson,
            Services = resumed.Services, Logger = resumed.Logger,
            ReportStage = async stage =>
            {
                if (stage != "preparingFiles" || preparing) return;
                preparing = true;
                if (!replaceBeforeSnapshot) return;
                var modifiedAt = File.GetLastWriteTimeUtc(source);
                await File.WriteAllBytesAsync(source, modified);
                File.SetLastWriteTimeUtc(source, modifiedAt);
            }
        };
        var answer = JsonSerializer.Serialize(new CloudFileProcessingSignal
        {
            TargetId = workset.Targets.Single().Id, ArchivePolicy = "instructions",
            ExtractionPlanJson = """{"requirement":"required","steps":[{"id":"rename","op":"renameExtension","input":"download","extension":".txt"}]}"""
        }, WorkflowJson.Options);
        var outcome = await activity.ResumeAsync(checkedContext, wait.Suspension.Item, answer, default);
        Assert.IsTrue(preparing);
        if (replaceBeforeSnapshot)
        {
            if (outcome.Replacement is DownloadWorkset completed)
            {
                var output = completed.Targets.Single().ProcessedFiles.Single();
                CollectionAssert.AreEqual(modified, await File.ReadAllBytesAsync(output));
                Assert.Fail("The changed private copy was committed as a new result although its SHA-256 differs from the registered source.");
            }
            Assert.IsNotNull(outcome.Suspension);
            Assert.AreEqual("NeedsReconciliation", JsonDocument.Parse(outcome.Suspension.PromptJson!).RootElement.GetProperty("reason").GetString());
            Assert.AreEqual(0, await Db.Artifacts.CountAsync(a => a.Kind == "processedFiles"));
        }
        else
        {
            Assert.IsNull(outcome.Suspension, outcome.Suspension?.PromptJson);
            var target = ((DownloadWorkset)outcome.Replacement!).Targets.Single();
            var finished = await File.ReadAllBytesAsync(target.ProcessedFiles.Single());
            CollectionAssert.AreEqual(original, finished);
            Assert.AreEqual(registered.Manifest.Entries.Single().Sha256, Convert.ToHexString(SHA256.HashData(finished)).ToLowerInvariant());
        }
    }
}
