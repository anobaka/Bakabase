using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.Workflow.Artifacts;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionAssessmentCallView(int? Iteration, string InputKind, string? InputPreview,
    bool InputTruncated, string OutputKind, string? OutputPreview, bool OutputTruncated, string? ValidationError = null,
    string? CallId = null, string InputAvailability = "notRecorded", string OutputAvailability = "preview",
    bool InputRedacted = false, bool OutputRedacted = false, bool InputTextAvailable = false, bool OutputTextAvailable = false);
public sealed record AcquisitionAssessmentIoView(IReadOnlyList<AcquisitionAssessmentCallView> Calls);
public sealed record AcquisitionAssessmentTextPage(string Text, int Offset, int? NextOffset, int TotalCharacters,
    string ContentId, string Availability, bool Redacted);

internal static class AcquisitionAssessmentInspection
{
    private const int ReadLimit = AssessmentInspectionText.DocumentByteLimit + 1;
    internal static (string? Text, bool Truncated) PreviewText(string? text) => AssessmentInspectionText.Preview(text);

    internal static async Task<AcquisitionAssessmentIoView?> ReadAsync(BakabaseDbContext db,
        int taskId, int runId, string attemptId, CancellationToken ct, AssessmentCallSnapshotStore? snapshots = null)
    {
        var calls = await ReadCallsAsync(db, taskId, runId, attemptId, ct);
        if (calls == null) return null;
        var views = new List<AcquisitionAssessmentCallView>();
        foreach (var call in calls)
            views.Add((await ViewAsync(db, call.ArtifactId, call.Receipt, ct, snapshots)).View with {CallId = call.Id});
        return new(views);
    }

    internal static async Task<AcquisitionAssessmentTextPage?> ReadPageAsync(BakabaseDbContext db,
        int taskId, int runId, string attemptId, string callId, string direction, int offset, int length,
        string? contentId, CancellationToken ct, AssessmentCallSnapshotStore? snapshots = null)
    {
        if (direction is not ("input" or "output") || offset < 0 || length is < 1 or > 32768) return null;
        var calls = await ReadCallsAsync(db, taskId, runId, attemptId, ct);
        var call = calls?.SingleOrDefault(c => c.Id == callId);
        if (call == null) return null;
        var resolved = await ViewAsync(db, call.ArtifactId, call.Receipt, ct, snapshots);
        var document = direction == "input" ? resolved.Input : resolved.Output;
        if (document == null || offset > document.Text.Length) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.Text)));
        if (contentId != null && contentId != hash) return null;
        if (offset > 0 && offset < document.Text.Length && char.IsLowSurrogate(document.Text[offset])) return null;
        var end = Math.Min(document.Text.Length, offset + length);
        if (end < document.Text.Length && end > offset && char.IsHighSurrogate(document.Text[end - 1])) end--;
        return new(document.Text[offset..end], offset, end < document.Text.Length ? end : null,
            document.Text.Length, hash, document.Truncated ? "preview" : "complete", document.Redacted);
    }

    private sealed record Call(string Id, string? ArtifactId, AssessmentCallReceipt? Receipt);
    private static async Task<IReadOnlyList<Call>?> ReadCallsAsync(BakabaseDbContext db,
        int taskId, int runId, string attemptId, CancellationToken ct)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().Where(t => t.Id == taskId && t.BuilderId != null)
            .Select(t => new { t.WorkflowRunId }).SingleOrDefaultAsync(ct);
        if (task == null || string.IsNullOrWhiteSpace(attemptId) || attemptId.Length > 80) return null;
        var attempts = await AcquisitionStageHistory.ReadAsync(db, taskId, task.WorkflowRunId, ct, requestedRun: runId);
        var attempt = attempts.SingleOrDefault(a => a.Id == attemptId && a.Stage == "processing");
        if (attempt == null) return null;

        var ids = attempt.ExecutionIds.ToArray();
        var nodes = await db.WorkflowNodeExecutions.AsNoTracking().Where(n => n.WorkflowRunId == runId && ids.Contains(n.Id)
            && n.Kind == AssessFileArtifactDeliveryActivity.ActivityKind).OrderBy(n => n.Id)
            .Select(n => new { n.Id, n.OutputsJson, n.InputsJson }).ToListAsync(ct);
        var calls = new List<Call>();
        foreach (var node in nodes)
        {
            var blocks = WorkflowOutputMetadataCapture.Read(node.OutputsJson, includeDetails: true);
            var receipts = blocks.Select(AssessmentCallReceipt.Read).OfType<AssessmentCallReceipt>().ToArray();
            if (receipts.Length != 0)
            {
                for (var index = 0; index < receipts.Length; index++)
                    calls.Add(new($"{node.Id}:{index}", receipts[index].ArtifactId, receipts[index]));
            }
            else
            {
                // The recorded inputs predate call receipts. Show the node's received inventory only
                // when it is unambiguous. Never attach a global AI log by timestamp, model or task title.
                static bool DocumentsCall(Bakabase.Modules.Workflow.Abstractions.Models.Domain.WorkflowOutputBlock b) =>
                    b.Key == "adaptive-assessment" && (b.Code?.StartsWith("LLM_", StringComparison.Ordinal) == true ||
                    b.Code is "INCOMPLETE_FILE_INVENTORY" or "ARCHIVE_FAMILY_REVIEW_REQUIRED");
                if (!blocks.Any(DocumentsCall) && node.Id != attempt.SavedAssessmentExecutionId) continue;
                var files = WorkflowOutputMetadataCapture.Read(node.InputsJson).Where(b => b.Kind == "artifact" &&
                    b.Key.EndsWith(":processed", StringComparison.Ordinal)).Select(b => b.ArtifactId).Distinct().ToArray();
                calls.Add(new($"{node.Id}:legacy", files.Length == 1 ? files[0] : null, null));
            }
        }
        return calls;
    }

    private sealed record Resolved(AcquisitionAssessmentCallView View, AssessmentInspectionText.Document? Input,
        AssessmentInspectionText.Document? Output);
    private static async Task<Resolved> ViewAsync(BakabaseDbContext db,
        string? artifactId, AssessmentCallReceipt? receipt, CancellationToken ct, AssessmentCallSnapshotStore? snapshots)
    {
        var outputKind = receipt?.OutputKind ?? "notRecorded";
        string? input = null, output = receipt?.OutputPreview ?? receipt?.Shape;
        bool inputTruncated = false, outputTruncated = receipt?.OutputPreview != null ? receipt.OutputTruncated : receipt?.ShapeTruncated ?? false;
        string inputKind = "unavailable";
        AssessmentInspectionText.Document? inputDocument = null, outputDocument = null;
        if (snapshots != null && receipt != null)
        {
            var snapshot = await snapshots.ReadAsync(receipt.SnapshotId, receipt.ArtifactId, receipt.Iteration, ct);
            inputDocument = snapshot?.Input;
            outputDocument = snapshot?.Output;
        }
        if (receipt?.UsageId is {} usage)
        {
            var audit = await db.Set<LlmUsageLogDbModel>().AsNoTracking().Where(l => l.Id == usage && l.Feature == "FileProcessor")
                .Select(l => new { Request = l.RequestSummary == null ? null : l.RequestSummary.Substring(0, ReadLimit),
                    Response = l.ResponseSummary == null ? null : l.ResponseSummary.Substring(0, ReadLimit) }).SingleOrDefaultAsync(ct);
            // An exact audit reference may recover complete historical text. Prefer it to an
            // old truncated receipt, never select a global log by timestamp or model name.
            if (!string.IsNullOrEmpty(audit?.Request) && (inputDocument == null || inputDocument.Truncated))
            {
                inputDocument = AssessmentInspectionText.Sanitize(audit.Request);
                if (audit.Request.Length >= ReadLimit) inputDocument = inputDocument with {Truncated = true};
            }
            if (!string.IsNullOrEmpty(audit?.Response) && (outputDocument == null || outputDocument.Truncated))
            {
                outputDocument = AssessmentInspectionText.Sanitize(audit.Response);
                if (audit.Response.Length >= ReadLimit) outputDocument = outputDocument with {Truncated = true};
            }
        }
        if (inputDocument != null)
        {
            input = inputDocument.Text[..Math.Min(inputDocument.Text.Length, AssessmentInspectionText.PreviewLimit)];
            inputTruncated = inputDocument.Truncated || input.Length < inputDocument.Text.Length;
            inputKind = "modelInput";
        }
        if (outputDocument != null)
        {
            output = outputDocument.Text[..Math.Min(outputDocument.Text.Length, AssessmentInspectionText.PreviewLimit)];
            outputTruncated = outputDocument.Truncated || output.Length < outputDocument.Text.Length;
            outputKind = "modelOutput";
        }
        if (input == null && artifactId != null)
        {
            // Read a bounded prefix of the registered manifest even if the volume is offline
            // or the original files have since been moved. Never traverse the file system.
            var rows = await db.Database.SqlQuery<string>($"""
                SELECT json_object('relativePath', substr(json_extract(e.value, '$.relativePath'),1,4096),
                                   'length', json_extract(e.value, '$.length'), 'sha256', '') AS Value
                FROM Artifacts a, json_each(CASE WHEN json_valid(a.ManifestJson) THEN a.ManifestJson END,'$.entries') e
                WHERE a.Id = {artifactId} AND json_type(e.value,'$.relativePath') = 'text' AND json_type(e.value,'$.length') = 'integer'
                LIMIT 201
                """).ToListAsync(ct);
            var entries = rows.Select(row => JsonSerializer.Deserialize<ArtifactManifestEntry>(row,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToArray();
            if (entries.Length != 0)
            {
                var inventory = AdaptiveFileProcessingAssessmentService.DescribeManifest(new(entries, "inspection"));
                (input, inputTruncated) = PreviewText(inventory.Json);
                inputTruncated |= inventory.Truncated;
            }
            if (input != null) inputKind = "recordedFiles";
        }
        static string Availability(AssessmentInspectionText.Document? document, string? preview) =>
            document != null ? document.Truncated ? "preview" : "complete" : preview == null ? "notRecorded" : "preview";
        return new(new(receipt?.Iteration, inputKind, input, inputTruncated, outputKind, output, outputTruncated, receipt?.ValidationError,
            InputAvailability: Availability(inputDocument, input), OutputAvailability: Availability(outputDocument, output),
            InputRedacted: inputDocument?.Redacted ?? false, OutputRedacted: outputDocument?.Redacted ?? false,
            InputTextAvailable: inputDocument != null, OutputTextAvailable: outputDocument != null), inputDocument, outputDocument);
    }
}
