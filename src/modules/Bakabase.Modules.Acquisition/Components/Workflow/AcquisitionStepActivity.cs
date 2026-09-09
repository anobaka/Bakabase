using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Acquisition.Components.Workflow;

/// <summary>
/// One acquisition step, wearing the workflow engine's clothes. It is the only file in the module
/// that knows both vocabularies, which is what keeps <see cref="IAcquisitionStep"/> free of workflow
/// types and lets the steps be hosted by something else entirely.
/// <para>
/// The translation is small because the two contracts were designed to meet: Continue and Skip both
/// keep the item, Suspend maps to the engine's own suspension, and Fail throws — the engine's way of
/// saying a step did not work.
/// </para>
/// </summary>
public class AcquisitionStepActivity(IAcquisitionStep step) : IResumableWorkflowActivity
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => step.Kind;
    public string DisplayName => step.DisplayName;
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public string Group => AcquisitionWorkflowKinds.ActivityGroup;

    public IReadOnlyList<string> AcceptedInputItemTypes => [AcquisitionWorkflowKinds.ItemAcquisition];

    // Every step takes an acquisition item and produces one: the item is enriched, never replaced
    // with something of a different shape. That is what lets steps be reordered freely in a recipe.
    public WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.Passthrough;

    /// <summary>
    /// Steps move files and write to the library, but always into this run's own directory or the
    /// place the user chose — and placement is the last thing before materialization, which the
    /// user sees. Declaring them destructive would forbid an AI transform from ever preceding one,
    /// which is exactly the "let a model clean up the folder name" shape a recipe wants.
    /// </summary>
    public bool IsDestructive => false;

    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item,
        CancellationToken ct)
    {
        var (workItem, stepCtx) = Prepare(ctx, item);

        return await Translate(ctx, await step.ExecuteAsync(stepCtx, workItem, ct), ct);
    }

    public async Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item,
        string signalJson, CancellationToken ct)
    {
        var (workItem, stepCtx) = Prepare(ctx, item);

        AcquisitionResumeSignal signal;
        try
        {
            signal = JsonSerializer.Deserialize<AcquisitionResumeSignal>(signalJson, Json)
                     ?? throw new WorkflowActivityConfigException(
                         $"The signal sent to step '{step.Kind}' was empty.");
        }
        catch (JsonException ex)
        {
            throw new WorkflowActivityConfigException(
                $"The signal sent to step '{step.Kind}' is not an acquisition signal: {ex.Message}", ex);
        }

        return await Translate(ctx, await step.ResumeAsync(stepCtx, workItem, signal, ct), ct);
    }

    private (AcquisitionWorkItem Item, AcquisitionStepContext Context) Prepare(
        WorkflowExecutionContext ctx, object item)
    {
        if (item is not AcquisitionWorkItem workItem)
        {
            // The editor's typed-chain validation makes this unreachable; reaching it means the
            // engine handed a step something no acquisition ever produced.
            throw new WorkflowActivityConfigException(
                $"Step '{step.Kind}' received a {item.GetType().Name}, not an {nameof(AcquisitionWorkItem)}.");
        }

        var stepCtx = new AcquisitionStepContext(
            ctx.Services,
            ctx.Logger,
            ctx.ReportProgress,
            workItem.WorkingDirectory,
            ctx.ActivityConfigJson);

        return (workItem, stepCtx);
    }

    private async Task<WorkflowItemOutcome> Translate(WorkflowExecutionContext ctx,
        AcquisitionStepOutcome outcome, CancellationToken ct)
    {
        switch (outcome)
        {
            case AcquisitionStepOutcome.Continue c:
                await Sync(ctx, c.Item, AcquisitionStatus.Running, null, null, ct);

                return WorkflowItemOutcome.ReplaceWith(c.Item);

            case AcquisitionStepOutcome.Skip s:
                // Not applicable is not a failure — there was no archive to unpack. The chain
                // carries on with the item exactly as the step left it.
                ctx.Logger.LogInformation("[Acquisition] Step {Kind} did not apply: {Why}", step.Kind, s.Why);
                await Sync(ctx, s.Item, AcquisitionStatus.Running, null, null, ct);

                return WorkflowItemOutcome.ReplaceWith(s.Item);

            case AcquisitionStepOutcome.Suspend sp:
                await Sync(ctx, sp.Item, AcquisitionStatus.Waiting, sp.Reason, null, ct);

                return WorkflowItemOutcome.Suspend(
                    new WorkflowSuspension(sp.Reason.ToString(), sp.PromptJson, sp.Item));

            case AcquisitionStepOutcome.Fail f:
                await Sync(ctx, null, AcquisitionStatus.Failed, null, f.Message, ct);

                // Throwing is how the engine is told a step did not work; the run's own error
                // handling decides whether that ends the run.
                throw f.Error is null
                    ? new AcquisitionStepException(step.Kind, f.Message)
                    : new AcquisitionStepException(step.Kind, f.Message, f.Error);

            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome,
                    "Unknown acquisition step outcome.");
        }
    }

    /// <summary>
    /// Mirrors what just happened onto the task row. Best-effort by design: a run that succeeded
    /// must not be reported as failed because the index could not be written.
    /// </summary>
    private async Task Sync(WorkflowExecutionContext ctx, AcquisitionWorkItem? item,
        AcquisitionStatus status, AcquisitionWaitReason? waitReason, string? error, CancellationToken ct)
    {
        if (ctx.Payload is not AcquisitionRequestedPayload payload) return;

        try
        {
            var tasks = ctx.Services.GetService<IAcquisitionService>();

            if (tasks == null) return;

            await tasks.SyncFromRunAsync(payload.TaskId, ctx.RunId, status, waitReason,
                item?.TargetDirectory, item?.Purchases, error, ct);
        }
        catch (Exception ex)
        {
            ctx.Logger.LogWarning(ex,
                "[Acquisition] Could not update task {TaskId} after step {Kind}", payload.TaskId, step.Kind);
        }
    }
}

/// <summary>A step reporting <c>Fail</c>, carried into the engine's error handling.</summary>
public class AcquisitionStepException : Exception
{
    public AcquisitionStepException(string kind, string message) : base($"[{kind}] {message}")
    {
        Kind = kind;
    }

    public AcquisitionStepException(string kind, string message, Exception inner)
        : base($"[{kind}] {message}", inner)
    {
        Kind = kind;
    }

    public string Kind { get; }
}
