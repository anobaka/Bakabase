using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Workflow.Text;

namespace Bakabase.Service.Components.Workflow.Activities.Transforms.Text;

/// <summary>
/// Captures, never rewrites (docs/file-cleaning-workflow.html §3.4): named regex groups matched
/// against the working text are written into the item's variable bag — the group name IS the
/// variable name — for a later template to read. "S01E{ep}" out of one directory level, the
/// title out of another, combined levels later: that's this node plus expansion.
/// </summary>
public class TextCaptureActivity : IWorkflowActivity
{
    /// <summary>Bounds user-supplied patterns — a catastrophic backtrack must not hang a run.</summary>
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(2);

    public string Kind { get; } = TextWorkflowKinds.TransformCapture;
    public string DisplayName => "Capture variables";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public string Group => WorkflowActivityGroups.Text;
    public Type? AcceptedItemInterface => typeof(ITextWorkpiece);

    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        var workpiece = TextActivityHelpers.Workpiece(Kind, item);
        var config = ctx.GetConfig<Config>();
        if (string.IsNullOrWhiteSpace(config?.Pattern))
        {
            throw new WorkflowActivityConfigException($"{Kind} needs a regex pattern configured.");
        }

        Regex regex;
        try
        {
            regex = new Regex(config.Pattern, RegexOptions.None, PatternTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new WorkflowActivityConfigException($"{Kind}: the pattern does not compile: {ex.Message}", ex);
        }

        var match = regex.Match(workpiece.WorkingText);
        if (!match.Success)
        {
            return config.OnMiss == CaptureMissBehavior.Fail
                ? throw new InvalidOperationException(
                    $"The pattern matched nothing in \"{workpiece.WorkingText}\".")
                : Task.FromResult(WorkflowItemOutcome.KeepItem);
        }

        foreach (var group in match.Groups.Keys)
        {
            // Numbered groups are regex plumbing, not variables.
            if (int.TryParse(group, out _)) continue;
            if (match.Groups[group].Success) ctx.Variables[group] = match.Groups[group].Value;
        }

        return Task.FromResult(WorkflowItemOutcome.KeepItem);
    }

    public record Config
    {
        /// <summary>Regex with named groups, e.g. <c>S(?&lt;season&gt;\d+)E(?&lt;ep&gt;\d+)</c>.</summary>
        public string? Pattern { get; init; }

        public CaptureMissBehavior OnMiss { get; init; } = CaptureMissBehavior.Ignore;
    }
}

/// <summary>What a capture does when its pattern matches nothing.</summary>
public enum CaptureMissBehavior
{
    /// <summary>Keep going with no variables written — downstream requiredVars decide.</summary>
    Ignore = 1,

    /// <summary>Treat the item as failed (the step's OnItemError policy then applies).</summary>
    Fail = 2,
}
