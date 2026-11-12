using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public class AcquisitionAssessmentInspectionTests
{
    [TestMethod]
    public async Task Read_matches_actual_task_run_and_attempt_hides_detail_refs_and_redacts_audited_text()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        var db = services.GetRequiredService<BakabaseDbContext>();
        var task = new AcquisitionTaskDbModel { BuilderId = "prepared", DeletedAt = DateTime.UtcNow };
        var foreign = new AcquisitionTaskDbModel { BuilderId = "prepared" };
        var log = new LlmUsageLogDbModel {Feature = "FileProcessor",
            RequestSummary = "[system] rules\n\n[user] File inventory:\nfiles demo.zip\n\nContext evidence: private context",
            ResponseSummary = "Broken JSON { \"status\": \"continue\", \"password\": \"secret123\", https://example.com/test?sig=secret456"};
        db.AddRange(task, foreign, log);
        await db.SaveChangesAsync();
        var detail = AssessmentCallReceipt.Create("first", 0, log.Id, log.ResponseSummary, true).Block("part");
        var hiddenDetail = AssessmentCallReceipt.Create("first", 1, null, null, false).Block("part");
        var metadata = new WorkflowOutputMetadataCapture();
        metadata.Add([detail, hiddenDetail]);
        Assert.AreEqual(0, WorkflowOutputMetadataCapture.Read(metadata.Json).Count);

        var input = """[{"kind":"artifact","key":"part:processed","artifactId":"first"}]""";
        var old = new WorkflowNodeExecutionDbModel { Attempt = 1, Kind = "fs.assessDelivery", InputsJson = input, Status = "waiting",
            OutputsJson = """[{"kind":"warning","key":"adaptive-assessment","code":"LLM_DECISION_INVALID_JSON"}]""" };
        var retry = new WorkflowNodeExecutionDbModel { Kind = old.Kind, InputsJson = input, Status = "waiting",
            Attempt = 2, ResumeKind = "signal", OutputsJson = metadata.Json };
        var manual = new WorkflowNodeExecutionDbModel { Kind = old.Kind, InputsJson = input, Status = "succeeded",
            Attempt = 3, ResumeKind = "signal" };
        var manifest = new ArtifactDbModel { Id = "first", RootPath = "/retired", Kind = "processedFiles",
            ManifestJson = """{"entries":[{"relativePath":"demo.zip","length":42,"sha256":""}]}""" };
        var run = new WorkflowRunDbModel { Sources = [new() {Module = "acquisition", EntityKind = "task",
            EntityId = task.Id.ToString()}], NodeExecutions = [old,retry,manual]};
        db.AddRange(run, manifest);
        await db.SaveChangesAsync();

        async Task<AcquisitionAssessmentIoView?> Read(int owner, long node) =>
            await AcquisitionAssessmentInspection.ReadAsync(db, owner, run.Id, $"{run.Id}/{node}", default);
        var legacy = (await Read(task.Id, old.Id))!.Calls.Single();
        Assert.AreEqual("recordedFiles", legacy.InputKind);
        StringAssert.Contains(legacy.InputPreview!, "demo.zip");
        StringAssert.Contains(legacy.InputPreview!, "\"bytes\":42");
        Assert.AreEqual("notRecorded", legacy.OutputKind);
        Assert.IsNull(legacy.OutputPreview);
        var recorded = (await Read(task.Id, retry.Id))!.Calls;
        Assert.AreEqual(2,recorded.Count);
        Assert.AreEqual("modelInput", recorded[0].InputKind);
        Assert.IsFalse(recorded[0].InputPreview!.Contains("private context"));
        Assert.AreEqual("modelOutput", recorded[0].OutputKind);
        StringAssert.Contains(recorded[0].OutputPreview!, "Broken JSON");
        Assert.IsFalse(recorded[0].OutputPreview!.Contains("secret"));
        Assert.AreEqual("noResponse", recorded[1].OutputKind);
        Assert.AreEqual(0, (await Read(task.Id, manual.Id))!.Calls.Count);
        Assert.IsNull(await Read(foreign.Id, retry.Id));
        Assert.IsNull(await Read(task.Id, -1));
        foreign.WorkflowRunId = run.Id;
        await db.SaveChangesAsync();
        Assert.IsNull(await Read(task.Id, retry.Id));
    }

    [TestMethod]
    public void Task_local_reply_survives_metadata_roundtrip_without_global_audit_and_keeps_legacy_shapes_readable()
    {
        var capture = new WorkflowOutputMetadataCapture();
        var raw = "{\"status\":\"continue\",\"reason\":\"" + new string('文', 7000) +
            "\",\"operations\":[{\"type\":\"move\",\"inputId\":27,\"destination\":\"audio\"}],\"token\":\"secret-canary\"}";
        for (var i = 0; i < 5; i++)
            capture.Add([AssessmentCallReceipt.Create("artifact", i, null, raw, true, "operations: invalid").Block("part")]);
        Assert.IsFalse(capture.Truncated);
        Assert.AreEqual(0, WorkflowOutputMetadataCapture.Read(capture.Json).Count);
        var details = WorkflowOutputMetadataCapture.Read(capture.Json, includeDetails: true);
        Assert.AreEqual(5, details.Count);
        foreach (var block in details)
        {
            var receipt = AssessmentCallReceipt.Read(block)!;
            Assert.IsNotNull(receipt);
            Assert.AreEqual("modelOutput", receipt.OutputKind);
            StringAssert.Contains(receipt.OutputPreview!, "\"inputId\":27");
            StringAssert.Contains(receipt.OutputPreview!, "audio");
            Assert.IsFalse(receipt.OutputPreview!.Contains("secret-canary"));
            Assert.AreEqual("operations: invalid", receipt.ValidationError);
        }
        var legacy = AssessmentCallReceipt.Read(new() {Kind = "detail", Key = "assessment:old:1",
            Value = """{"artifactId":"old","iteration":1,"outputKind":"structure","shape":"{?}","shapeTruncated":false}"""});
        Assert.IsNotNull(legacy);
        Assert.AreEqual("structure", legacy.OutputKind);
        Assert.IsNull(legacy.OutputPreview);
    }

    [TestMethod]
    public void Reply_keeps_actual_values_with_bounded_credential_redaction_and_distinguishes_empty_and_absent_replies()
    {
        var shape = AssessmentCallReceipt.Create("id", 0, null, """{"status":"continue","reason":"sensitive phrase","operations":[{"type":"extract","fileIndex":9876""", true);
        Assert.AreEqual("modelOutput", shape.OutputKind);
        StringAssert.Contains(shape.OutputPreview!, "sensitive phrase");
        StringAssert.Contains(shape.OutputPreview!, "9876");
        StringAssert.Contains(shape.Shape!, """"status":"continue"""");
        StringAssert.Contains(shape.Shape!, """"type":"extract"""");
        Assert.IsFalse(shape.Shape!.Contains("sensitive"));
        Assert.IsFalse(shape.Shape!.Contains("9876"));
        Assert.AreEqual("empty", AssessmentCallReceipt.Create("id", 0, null, "", true).OutputKind);
        Assert.AreEqual("noResponse", AssessmentCallReceipt.Create("id", 0, null, null, false).OutputKind);
        var clipped = AssessmentCallReceipt.Create("id", 0, null, new string('[', 10000), true);
        Assert.IsTrue(clipped.ShapeTruncated);
        Assert.IsTrue(clipped.Shape!.Length <= 500);
        var safe = AcquisitionAssessmentInspection.PreviewText("""Authorization: Bearer abc234, {"api_key":"leak","pwd":"hidden"}""");
        Assert.IsFalse(safe.Text!.Contains("abc234"));
        Assert.IsFalse(safe.Text.Contains("leak"));
        Assert.IsFalse(safe.Text.Contains("hidden"));
        foreach (var text in new[] {"密码是 secret-canary", "password is secret-canary", """{"\u0070assword":"secret-canary","inputId":42}"""})
            Assert.IsFalse(AssessmentInspectionText.Preview(text).Text!.Contains("secret-canary"));
    }
}
