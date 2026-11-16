using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>A bounded task-local reply for on-demand inspection plus an optional audit reference.
/// The receipt is saved on the exact node invocation, including on cache hits and provider failures.</summary>
internal sealed record AssessmentCallReceipt(string ArtifactId, int Iteration, long? UsageId, string OutputKind,
    string? Shape, bool ShapeTruncated, string? OutputPreview = null, bool OutputTruncated = false,
    string? ValidationError = null, string? SnapshotId = null)
{
    private static readonly HashSet<string> Vocabulary = new(StringComparer.Ordinal)
    { "status", "reason", "confidence", "operations", "type", "file", "fileIndex", "input", "destination",
      "retain", "deleteAfterSuccess", "continue", "final", "humanReview", "unsupported", "extract", "rename", "move" };

    internal static AssessmentCallReceipt Create(string artifact, int iteration, long? usage, string? raw, bool received, string? validationError = null)
    {
        if (!received || string.IsNullOrEmpty(raw))
            return new(artifact, iteration + 1, usage, received ? "empty" : "noResponse", null, false);

        // Never preserve unclassified tokens, including text in malformed or unquoted model output.
        // A small sketch can still distinguish fenced JSON, a cut-off array and a prose-only response.
        const int limit = 480;
        var result = new StringBuilder();
        var end = Math.Min(2048, raw.Length);
        int i = 0;
        while (i < end && result.Length < limit)
        {
            var start = i;
            var c = raw[i++];
            if (c == '"')
            {
                var escaped = false;
                while (i < end)
                {
                    var current = raw[i++];
                    if (!escaped && current == '"') break;
                    escaped = !escaped && current == '\\';
                }
                var closed = i > start + 1 && raw[i - 1] == '"';
                var length = i - start - (closed ? 2 : 1);
                var allowed = length < 40 && Vocabulary.Contains(raw.Substring(start + 1, length));
                result.Append('"').Append(allowed ? raw.Substring(start + 1, length) : "?");
                if (closed) result.Append('"');
            }
            else if (char.IsWhiteSpace(c))
            {
                if (result.Length > 0 && result[^1] != ' ') result.Append(' ');
            }
            else if ("{}[]:,`".Contains(c)) result.Append(c);
            else
            {
                while (i < end && !char.IsWhiteSpace(raw[i]) && !"\"{}[]:,`".Contains(raw[i])) i++;
                result.Append('?');
            }
        }
        var preview = AssessmentInspectionText.Preview(raw);
        return new(artifact, iteration + 1, usage, "modelOutput", result.ToString(), i < raw.Length,
            preview.Text, preview.Truncated, validationError);
    }

    internal WorkflowOutputBlock Block(string targetId) => new()
    {
        Kind = "detail", Key = $"assessment:{targetId}:{Iteration}",
        Value = JsonSerializer.Serialize(this, WorkflowJson.Options)
    };

    internal static AssessmentCallReceipt? Read(WorkflowOutputBlock block)
    {
        if (block.Kind != "detail" || !block.Key.StartsWith("assessment:", StringComparison.Ordinal) || block.Value == null) return null;
        try
        {
            var record = JsonSerializer.Deserialize<AssessmentCallReceipt>(block.Value, WorkflowJson.Options);
            return record is {Iteration: >= 1 and <= AdaptiveFileProcessingAssessmentService.MaxCallsPerTask,
                OutputKind: "empty" or "noResponse" or "structure" or "modelOutput", ArtifactId.Length: > 0 and <= 256} ? record : null;
        }
        catch (JsonException) { return null; }
    }
}
