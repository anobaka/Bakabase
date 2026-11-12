using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;

namespace Bakabase.Modules.Workflow.Components;

/// <summary>Bounded, allowlisted inspection data. No reflection over execution items.</summary>
public sealed class WorkflowOutputMetadataCapture
{
    public const int MaximumBlocks = 64;
    public const int MaximumCharacters = 512 * 1024;
    private readonly List<WorkflowOutputBlock> _outputs = [];
    public IReadOnlyList<WorkflowOutputBlock> Outputs => _outputs;
    public bool Truncated { get; private set; }
    public string Json => JsonSerializer.Serialize(_outputs, WorkflowJson.Options);

    public void AddItem(object item, string activityKind)
    {
        if (item is not IWorkflowOutputMetadata provider) return;
        try { Add(provider.GetWorkflowOutputMetadata(activityKind)); }
        catch
        {
            // Inspection must not turn a successful side effect into a failed/replayed activity.
            Add([new() {Kind = "warning", Key = "metadata-unavailable", Code = "WORKFLOW_METADATA_UNAVAILABLE", Severity = "warning"}]);
        }
    }

    public void Add(IEnumerable<WorkflowOutputBlock> outputs)
    {
        var inspected = 0;
        foreach (var output in outputs)
        {
            if (++inspected > MaximumBlocks * 2) { Truncated = true; break; }
            var safe = Normalize(output);
            if (safe == null) { Truncated = true; continue; }
            var index = _outputs.FindIndex(o => o.Kind == safe.Kind && o.Key == safe.Key);
            if (index < 0 && _outputs.Count >= MaximumBlocks) { Truncated = true; continue; }
            var prior = index >= 0 ? _outputs[index] : null;
            if (index >= 0) _outputs[index] = safe; else _outputs.Add(safe);
            if (Json.Length <= MaximumCharacters) continue;
            if (index >= 0) _outputs[index] = prior!; else _outputs.RemoveAt(_outputs.Count - 1);
            Truncated = true;
        }
    }

    public static WorkflowOutputBlock? Normalize(WorkflowOutputBlock? block)
    {
        if (block == null || string.IsNullOrWhiteSpace(block.Key)) return null;
        string? Text(string? text, int limit = 1024) => string.IsNullOrWhiteSpace(text) ? null :
            new string(text.Where(c => !char.IsControl(c)).Take(limit).ToArray());
        var common = new WorkflowOutputBlock {Kind = block.Kind, Key = Text(block.Key, 256)!,
            LabelKey = Text(block.LabelKey, 256), Label = Text(block.Label)};
        return block.Kind switch
        {
            "artifact" when !string.IsNullOrWhiteSpace(block.ArtifactId) => common with
            {
                ArtifactId = Text(block.ArtifactId, 256), OperationId = Text(block.OperationId, 256),
                Directory = Text(block.Directory, 4096), FileCount = block.FileCount >= 0 ? block.FileCount : null,
                TotalBytes = block.TotalBytes >= 0 ? block.TotalBytes : null, State = Text(block.State, 64),
                CleanupReceiptId = Text(block.CleanupReceiptId, 256), CleanupStatus = Text(block.CleanupStatus, 64)
            },
            "remote" => common with {ProviderId = Text(block.ProviderId, 128), MethodId = Text(block.MethodId, 128),
                EngineId = Text(block.EngineId, 128), RemotePath = Text(block.RemotePath, 4096), Url = SafeUrl(block.Url)},
            "task" when !string.IsNullOrWhiteSpace(block.TaskId) => common with
                {TaskKind = Text(block.TaskKind, 128), TaskId = Text(block.TaskId, 256)},
            // Internal bounded inspection receipts are fetched on demand, never rendered as generic output.
            "detail" => common with {Value = Text(block.Value, 64 * 1024)},
            "scalar" => common with {Value = Text(block.Value, 2048)},
            "warning" => common with {Code = Text(block.Code, 128),
                Severity = block.Severity is "info" or "error" ? block.Severity : "warning", Message = Text(block.Message)},
            _ => null
        };
    }

    private static string? SafeUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo)) return null;
        // Query/fragment can carry share passwords, OAuth state or expiring signatures.
        var safe = uri.GetLeftPart(UriPartial.Path);
        return safe.Length <= 4096 ? safe : null;
    }

    public static List<WorkflowOutputBlock> Read(string? json, bool includeDetails = false)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var capture = new WorkflowOutputMetadataCapture();
            capture.Add(JsonSerializer.Deserialize<List<WorkflowOutputBlock>>(json, WorkflowJson.Options) ?? []);
            return capture.Outputs.Where(b => includeDetails || b.Kind != "detail").ToList();
        }
        catch (JsonException) { return []; }
    }
}
