using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Workflow.Text;

namespace Bakabase.Service.Components.Workflow.Activities.Transforms.Text;

/// <summary>
/// Rebuilds the working text from a template (docs/file-cleaning-workflow.html §3.4) — the
/// cross-level composition step: "{var:title} - S01E{var:ep:pad(2)}.{var:extension}". Reads the
/// item's variable bag (captures) and its domain system variables (extension, parentName…);
/// <c>{originalText}</c> is the working text entering this step.
/// </summary>
public class TextTemplateActivity : IWorkflowActivity
{
    public string Kind { get; } = TextWorkflowKinds.TransformTemplate;
    public string DisplayName => "Rebuild from template";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public string Group => WorkflowActivityGroups.Text;
    public Type? AcceptedItemInterface => typeof(ITextWorkpiece);

    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        var workpiece = TextActivityHelpers.Workpiece(Kind, item);
        var config = ctx.GetConfig<Config>();
        if (string.IsNullOrWhiteSpace(config?.Template))
        {
            throw new WorkflowActivityConfigException($"{Kind} needs a template configured.");
        }

        var systemVariables = (item as IHasWorkflowSystemVariables)?.GetWorkflowSystemVariables();
        var bag = (IReadOnlyDictionary<string, string>) ctx.Variables;

        // requiredVars is the "missing must fail" gate; everything else interpolates to "".
        var missing = (config.RequiredVars ?? [])
            .Where(v => !WorkflowVariableInterpolator.CanResolve(v, bag, systemVariables))
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Required variable(s) [{string.Join(", ", missing)}] are not set for " +
                $"\"{workpiece.WorkingText}\" — an upstream capture didn't match.");
        }

        var newText = WorkflowVariableInterpolator.Interpolate(
            config.Template, bag, systemVariables, workpiece.WorkingText);

        return Task.FromResult(newText == workpiece.WorkingText
            ? WorkflowItemOutcome.KeepItem
            : WorkflowItemOutcome.ReplaceWith(workpiece.WithWorkingText(newText)));
    }

    public record Config
    {
        /// <summary>e.g. <c>{var:title} - E{var:ep:pad(2)}.{var:extension}</c>.</summary>
        public string? Template { get; init; }

        /// <summary>Variables that must resolve or the item fails (OnItemError applies).</summary>
        public List<string>? RequiredVars { get; init; }
    }
}
