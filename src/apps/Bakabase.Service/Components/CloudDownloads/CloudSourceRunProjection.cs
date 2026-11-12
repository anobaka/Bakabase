using System;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Controllers;
using Bakabase.Service.Components.Workflow;

namespace Bakabase.Service.Components.CloudDownloads;

public sealed class CloudSourceRunProjection : IWorkflowSourceRunProjection
{
    public void Enrich(WorkflowRunDbModel row, WorkflowSourceRunView view)
    {
        try
        {
            var json = row.CurrentItemJson ?? row.OutputItemsJson ?? row.PayloadJson;
            if (json == null) return;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array) root = root.EnumerateArray().FirstOrDefault();
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("item", out var item)) root = item;
            var acquisition = root;
            WorkflowSourceRunOutputs.AddLocation(view, "working", Text(acquisition, "workingDirectory"));
            if (acquisition.TryGetProperty("artifacts", out var artifactRefs) ||
                acquisition.TryGetProperty("variables", out artifactRefs))
            {
                WorkflowSourceRunOutputs.AddLocation(view, "download", Text(acquisition, "workingDirectory"), Text(artifactRefs, "artifact.inputId"));
                WorkflowSourceRunOutputs.AddLocation(view, "processed", Text(acquisition, "extractedDirectory"), Text(artifactRefs, "artifact.outputId"));
                WorkflowSourceRunOutputs.AddLocation(view, "published", Text(acquisition, "targetDirectory"), Text(artifactRefs, "artifact.publishedId"));
                if (Text(artifactRefs, "artifact.publishedId") != null)
                {
                    view.DeliveredDirectory = Text(acquisition, "targetDirectory");
                    view.CompletedFileCount = Number(acquisition, "filesCount") ?? ArrayCount(acquisition, "files") ?? 0;
                }
            }
            var nestedSummary = Text(root, "executionSummary");
            if (nestedSummary == null && root.TryGetProperty("variables", out var variables))
                nestedSummary = Text(variables, "download.workset");
            if (nestedSummary != null)
            {
                using var nested = JsonDocument.Parse(nestedSummary);
                root = nested.RootElement.Clone();
            }
            WorkflowSourceRunOutputs.AddLocation(view, "working", Text(root, "outputDirectory"));
            var values = root.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Array
                ? targets.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.Object &&
                    (!t.TryGetProperty("excluded", out var excluded) || excluded.ValueKind != JsonValueKind.True)).ToList()
                : [];
            foreach (var target in values)
            {
                WorkflowSourceRunOutputs.AddLocation(view, "download", Text(target, "downloadDirectory"), Text(target, "downloadArtifactId"));
                WorkflowSourceRunOutputs.AddLocation(view, "processed", Text(target, "processedDirectory"), Text(target, "processedArtifactId"));
                WorkflowSourceRunOutputs.AddLocation(view, "published", Text(target, "publishedDirectory") ?? Text(target, "publicationTargetDirectory"), Text(target, "publishedArtifactId"));
                WorkflowSourceRunProblems.Add(view, Text(target, "problem"));
            }
            view.DownloadTaskIds = values.SelectMany(t =>
            {
                if (t.TryGetProperty("transportTaskId", out var transport) && transport.ValueKind == JsonValueKind.String)
                    return new[] {transport.GetString()!};
                return t.TryGetProperty("downloadTaskId", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var number)
                    ? new[] {"site:" + number} : Array.Empty<string>();
            }).Distinct().ToList();
            if (values.Count > 0) view.CompletedFileCount = values.Sum(t => new[] {Number(t, "publishedFileCount"), ArrayCount(t, "publishedFiles"),
                Number(t, "processedFileCount"), ArrayCount(t, "processedFiles"), Number(t, "fileCount"), ArrayCount(t, "files")}.FirstOrDefault(n => n > 0) ?? 0);
            view.Reused = values.Any(t => Text(t, "executionStatus") is "reused" or "reusedFinal");
            view.DeliveredDirectory ??= values.Select(t => Text(t, "publishedDirectory")).FirstOrDefault(p => p != null);
            if (row.Status == WorkflowRunStatus.Success)
                view.Phase = view.DeliveredDirectory != null ? "delivered" : view.Reused ? "reused" : "completed";
            else if (row.Status == WorkflowRunStatus.Running)
                view.Phase = view.Run.ActivityKind switch {"fs.publishArtifacts" => "delivering",
                    "download.processFiles" or "fs.processArtifacts" => "processing",
                    "download.enqueueSupported" or "download.awaitCompletion" or "baidu.transfer" => "downloading", _ => "running"};
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
    }
    private static int? Number(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    private static int? ArrayCount(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 ? value.GetArrayLength() : null;
    private static string? Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
