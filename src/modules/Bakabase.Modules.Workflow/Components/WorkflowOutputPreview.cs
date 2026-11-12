using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Workflow.Components;

/// <summary>Small, valid JSON for inspection; never used to restore execution or replace full output.</summary>
public static class WorkflowOutputPreview
{
    public const int MaximumItems = 20;
    public const int MaximumCharacters = 64 * 1024;

    public static (string Json, bool Truncated) Capture(IEnumerable<object> items)
    {
        var serialized = new List<string>();
        var length = 2;
        var seen = 0;
        var truncated = false;
        foreach (var item in items)
        {
            if (++seen > MaximumItems) { truncated = true; break; }
            // The original item is an execution payload and may contain passwords or tokens.
            // Only an explicitly implemented safe inspection projection may cross this boundary.
            if (item is not IWorkflowOutputSummary summary) { truncated = true; continue; }
            string json;
            try
            {
                var compact = summary.ToWorkflowOutputSummary();
                json = JsonSerializer.Serialize(compact, compact?.GetType() ?? typeof(object), WorkflowJson.Options);
            }
            catch (Exception)
            {
                truncated = true;
                continue;
            }
            var required = json.Length + (serialized.Count == 0 ? 0 : 1);
            if (required > MaximumCharacters - length)
            {
                truncated = true;
                continue;
            }
            serialized.Add(json);
            length += required;
        }
        return ("[" + string.Join(",", serialized) + "]", truncated);
    }
}
