using System;
using System.Collections.Generic;
using System.Text.Json;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.DownloadIntents;

public sealed record ProcessingPlanPreflightResult(string Status, int StepCount,
    IReadOnlyList<DownloadRequirement> Issues, bool BlocksSubmission);

/// <summary>Read-only instruction checks. A complete plan is not a claim about future files.</summary>
public static class ProcessingPlanPreflight
{
    public static ProcessingPlanPreflightResult Inspect(string? planJson, bool consumesInstructions)
    {
        var issues = new List<DownloadRequirement>();
        string status;
        var count = 0;
        if (string.IsNullOrWhiteSpace(planJson))
        {
            status = "notProvided";
            issues.Add(new("PROCESSING_PLAN_REQUIRED", "processing",
                "This workflow needs file-processing instructions. Provide a plan or choose a download-only workflow.", Actions: ["editPlan"]));
        }
        else
        {
            try
            {
                if (planJson.Length > 131072) throw new InvalidOperationException("The processing plan is too long.");
                var plan = JsonSerializer.Deserialize<FileProcessingPlan>(planJson, JsonSerializerOptions.Web)
                           ?? throw new InvalidOperationException("Provide a file-processing plan object.");
                FileProcessingPlanValidator.Validate(plan);
                count = plan.Steps.Count;
                status = plan.Requirement switch
                {
                    "notRequired" => "notRequired",
                    "unknown" => "unknown",
                    "required" when count == 0 => "incomplete",
                    _ => "complete"
                };
                if (status is "unknown" or "incomplete")
                    issues.Add(new(status == "unknown" ? "PROCESSING_PLAN_UNKNOWN" : "PROCESSING_PLAN_INCOMPLETE", "processing",
                        "The file-processing instructions are incomplete. Confirm the required operations or choose a download-only workflow.", Actions: ["editPlan"]));
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
            {
                status = "invalid";
                // Do not echo JSON or passwords from parser/tool diagnostics.
                issues.Add(new("PROCESSING_PLAN_INVALID", "processing",
                    "The file-processing plan is invalid. Check its requirement, steps, input references and relative paths.", Actions: ["editPlan"]));
            }
        }
        return new(consumesInstructions ? status : "bypassed", count,
            consumesInstructions ? issues : [], consumesInstructions && issues.Count > 0);
    }
}
