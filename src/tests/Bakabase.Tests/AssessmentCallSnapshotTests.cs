using System.Text;
using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public class AssessmentCallSnapshotTests
{
    [TestMethod]
    public void Absent_password_and_token_remain_json_null_without_claiming_hidden_credentials()
    {
        var output = """{"status":"continue","operations":[{"type":"extract","inputId":1,"destination":null,"password":null}],"token":null,"nextIteration":2}""";
        var result = AssessmentInspectionText.Sanitize(output);

        Assert.IsFalse(result.Redacted);
        Assert.IsFalse(result.Truncated);
        using var parsed = JsonDocument.Parse(result.Text);
        var operation = parsed.RootElement.GetProperty("operations")[0];
        Assert.AreEqual(JsonValueKind.Null, operation.GetProperty("password").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, operation.GetProperty("destination").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, parsed.RootElement.GetProperty("token").ValueKind);
        Assert.AreEqual(2, parsed.RootElement.GetProperty("nextIteration").GetInt32());
    }

    [TestMethod]
    public void Null_secrets_and_present_credentials_are_distinguished_without_losing_later_fields()
    {
        var output = """{"operations":[{"password":null,"token":"token-canary","type":"extract"},{"password":"password-canary","token":null,"type":"rename"}],"status":"continue","authorization":{"secret":"nested-canary"}}""";
        var result = AssessmentInspectionText.Sanitize(output);

        Assert.IsTrue(result.Redacted);
        Assert.IsFalse(result.Truncated);
        Assert.IsFalse(result.Text.Contains("canary"));
        using var parsed = JsonDocument.Parse(result.Text);
        var operations = parsed.RootElement.GetProperty("operations");
        Assert.AreEqual(JsonValueKind.Null, operations[0].GetProperty("password").ValueKind);
        Assert.AreEqual("[…]", operations[0].GetProperty("token").GetString());
        Assert.AreEqual("extract", operations[0].GetProperty("type").GetString());
        Assert.AreEqual("[…]", operations[1].GetProperty("password").GetString());
        Assert.AreEqual(JsonValueKind.Null, operations[1].GetProperty("token").ValueKind);
        Assert.AreEqual("rename", operations[1].GetProperty("type").GetString());
        Assert.AreEqual("continue", parsed.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("[…]", parsed.RootElement.GetProperty("authorization").GetString());
    }

    [TestMethod]
    public void Structural_redaction_preserves_later_context_and_valid_json_instead_of_swallowing_the_line()
    {
        var context = """{"archivePassword":"secret-canary","executionEvidence":{"iteration":2,"completedProcessing":["extract"]},"url":"https://example.test/?token=hidden"}""";
        var result = AssessmentInspectionText.Sanitize("Context evidence:\n" + context + "\n\nAssessment iteration: 2");
        Assert.IsTrue(result.Redacted);
        Assert.IsFalse(result.Truncated);
        Assert.IsFalse(result.Text.Contains("secret-canary"));
        Assert.IsFalse(result.Text.Contains("token=hidden"));
        var json = result.Text.Split('\n')[1];
        using var parsed = JsonDocument.Parse(json);
        Assert.AreEqual("extract", parsed.RootElement.GetProperty("executionEvidence").GetProperty("completedProcessing")[0].GetString());
        StringAssert.Contains(result.Text, "Assessment iteration: 2");
    }

    [TestMethod]
    public async Task Independent_snapshot_is_bounded_private_and_validates_its_exact_receipt_without_global_audit()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new AssessmentCallSnapshotStore(root);
        try
        {
            var input = "[system] rules\n[user] " + new string('文', 12000) + "\nContext evidence:\n{\"password\":\"secret-canary\",\"completedProcessing\":[\"extract\"]}";
            var output = "{\"reason\":\"" + new string('v', 16000) + "\",\"status\":\"final\"}";
            var id = await store.SaveAsync("artifact", 2, input, output, default);
            var saved = (await store.ReadAsync(id, "artifact", 2, default))!;
            Assert.IsNotNull(saved);
            StringAssert.Contains(saved.Input!.Text, "completedProcessing");
            Assert.IsFalse(saved.Input.Text.Contains("secret-canary"));
            Assert.IsFalse(saved.Input.Truncated);
            Assert.AreEqual(output, saved.Output!.Text);
            Assert.IsNull(await store.ReadAsync(id, "other", 2, default));
            Assert.IsNull(await store.ReadAsync(id, "artifact", 3, default));
            Assert.IsNull(await store.ReadAsync("../outside", "artifact", 2, default));
            var bounded = AssessmentInspectionText.Sanitize(new string('文', AssessmentInspectionText.DocumentByteLimit));
            Assert.IsTrue(bounded.Truncated);
            Assert.IsTrue(Encoding.UTF8.GetByteCount(bounded.Text) <= AssessmentInspectionText.DocumentByteLimit);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task Exact_audit_recovers_long_text_and_pages_after_redaction_without_foreign_or_changed_document_access()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        var db = services.GetRequiredService<BakabaseDbContext>();
        var task = new AcquisitionTaskDbModel {BuilderId = "prepared"};
        var other = new AcquisitionTaskDbModel {BuilderId = "prepared"};
        var input = "[system] " + new string('s', 25000) + "\n\n[user] Context evidence:\n{\"executionEvidence\":\"succeeded\",\"token\":\"secret-canary\"}";
        var output = "{\"reason\":\"" + new string('x', 33000) + "\",\"password\":\"private-value\",\"status\":\"final\"}";
        var log = new LlmUsageLogDbModel {Feature = "FileProcessor", RequestSummary = input, ResponseSummary = output};
        db.AddRange(task, other, log); await db.SaveChangesAsync();
        var capture = new WorkflowOutputMetadataCapture();
        capture.Add([AssessmentCallReceipt.Create("artifact", 0, log.Id, output, true).Block("part")]);
        var node = new WorkflowNodeExecutionDbModel {Kind = "fs.assessDelivery", Status = "waiting", Attempt = 1, OutputsJson = capture.Json};
        var run = new WorkflowRunDbModel {Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}], NodeExecutions = [node]};
        db.Add(run); await db.SaveChangesAsync();
        var attempt = $"{run.Id}/{node.Id}";
        var view = (await AcquisitionAssessmentInspection.ReadAsync(db, task.Id, run.Id, attempt, default))!.Calls.Single();
        Assert.AreEqual("complete", view.InputAvailability);
        Assert.AreEqual("complete", view.OutputAvailability);
        Assert.IsTrue(view.OutputTextAvailable);
        Assert.IsTrue(view.OutputTruncated); // list remains a small preview
        var inputPage = await AcquisitionAssessmentInspection.ReadPageAsync(db, task.Id, run.Id, attempt, view.CallId!, "input", 0, 32768, null, default);
        StringAssert.Contains(inputPage!.Text, "executionEvidence");
        Assert.IsFalse(inputPage.Text.Contains("secret-canary"));
        var first = await AcquisitionAssessmentInspection.ReadPageAsync(db, task.Id, run.Id, attempt, view.CallId!, "output", 0, 32768, null, default);
        var second = await AcquisitionAssessmentInspection.ReadPageAsync(db, task.Id, run.Id, attempt, view.CallId!, "output", first!.NextOffset!.Value, 32768, first.ContentId, default);
        var full = first.Text + second!.Text;
        using var parsed = JsonDocument.Parse(full);
        Assert.AreEqual("final", parsed.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(full.Contains("private-value"));
        Assert.IsNull(second.NextOffset);
        Assert.IsNull(await AcquisitionAssessmentInspection.ReadPageAsync(db, other.Id, run.Id, attempt, view.CallId!, "output", 0, 32768, null, default));
        Assert.IsNull(await AcquisitionAssessmentInspection.ReadPageAsync(db, task.Id, run.Id, attempt, "foreign:0", "output", 0, 32768, null, default));
        Assert.IsNull(await AcquisitionAssessmentInspection.ReadPageAsync(db, task.Id, run.Id, attempt, view.CallId!, "output", 0, 32768, "stale", default));
    }
}
