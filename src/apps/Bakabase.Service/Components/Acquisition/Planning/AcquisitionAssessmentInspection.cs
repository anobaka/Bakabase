using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.Workflow.Artifacts;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionAssessmentCallView(int? Iteration, string InputKind, string? InputPreview,
    bool InputTruncated, string OutputKind, string? OutputPreview, bool OutputTruncated, string? ValidationError = null);
public sealed record AcquisitionAssessmentIoView(IReadOnlyList<AcquisitionAssessmentCallView> Calls);

internal static class AcquisitionAssessmentInspection
{
    private const int ReadLimit = AssessmentInspectionText.ReadLimit;
    internal static (string? Text, bool Truncated) PreviewText(string? text) => AssessmentInspectionText.Preview(text);

    internal static async Task<AcquisitionAssessmentIoView?> ReadAsync(BakabaseDbContext db,
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
        var calls = new List<AcquisitionAssessmentCallView>();
        foreach (var node in nodes)
        {
            var blocks = WorkflowOutputMetadataCapture.Read(node.OutputsJson, includeDetails: true);
            var receipts = blocks.Select(AssessmentCallReceipt.Read).OfType<AssessmentCallReceipt>().ToArray();
            if (receipts.Length != 0)
            {
                foreach (var receipt in receipts)
                    calls.Add(await ViewAsync(db, receipt.ArtifactId, receipt, ct));
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
                calls.Add(await ViewAsync(db, files.Length == 1 ? files[0] : null, null, ct));
            }
        }
        return new(calls);
    }

    private static async Task<AcquisitionAssessmentCallView> ViewAsync(BakabaseDbContext db,
        string? artifactId, AssessmentCallReceipt? receipt, CancellationToken ct)
    {
        var outputKind = receipt?.OutputKind ?? "notRecorded";
        string? input = null, output = receipt?.OutputPreview ?? receipt?.Shape;
        bool inputTruncated = false, outputTruncated = receipt?.OutputPreview != null ? receipt.OutputTruncated : receipt?.ShapeTruncated ?? false;
        string inputKind = "unavailable";
        if (receipt?.UsageId is {} usage)
        {
            var audit = await db.Set<LlmUsageLogDbModel>().AsNoTracking().Where(l => l.Id == usage && l.Feature == "FileProcessor")
                .Select(l => new { Request = l.RequestSummary == null ? null : l.RequestSummary.Substring(0, ReadLimit),
                    Response = l.ResponseSummary == null ? null : l.ResponseSummary.Substring(0, ReadLimit) }).SingleOrDefaultAsync(ct);
            var request = audit?.Request;
            var start = request?.IndexOf("[user] ", StringComparison.Ordinal) ?? -1;
            if (request != null && start >= 0)
            {
                // Only the actual submitted inventory, never the free-text source context or system prompt.
                var body = request[(start + 7)..];
                var context = body.IndexOf("\n\nContext evidence:", StringComparison.Ordinal);
                (input, inputTruncated) = PreviewText(context >= 0 ? body[..context] : body);
                inputTruncated |= context < 0 && request.Length == ReadLimit;
                if (input != null) inputKind = "modelInput";
            }
            if (outputKind != "modelOutput" && !string.IsNullOrEmpty(audit?.Response))
            {
                (output, outputTruncated) = PreviewText(audit.Response);
                outputKind = "modelOutput";
            }
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
        return new(receipt?.Iteration, inputKind, input, inputTruncated, outputKind, output, outputTruncated, receipt?.ValidationError);
    }
}
