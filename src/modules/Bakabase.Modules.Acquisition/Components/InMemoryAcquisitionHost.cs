using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>
/// Where a run got to.
/// </summary>
public enum AcquisitionRunState
{
    Completed = 1,

    /// <summary>Stopped at <see cref="AcquisitionHostRun.StepIndex"/> waiting for a signal.</summary>
    Waiting = 2,

    Failed = 3
}

/// <summary>
/// A run's whole state: the cursor, the item as it stands, and why it stopped if it did. This is
/// exactly what a real host persists after every step.
/// </summary>
public record AcquisitionHostRun
{
    public required AcquisitionRecipe Recipe { get; init; }
    public required AcquisitionWorkItem Item { get; set; }
    public int StepIndex { get; set; }
    public AcquisitionRunState State { get; set; }
    public AcquisitionWaitReason? WaitReason { get; set; }
    public string? WaitPromptJson { get; set; }
    public string? FailureMessage { get; set; }

    /// <summary>Each step that ran, in order, for tests to assert the path taken.</summary>
    public List<string> ExecutedKinds { get; } = [];

    /// <summary>Steps that said they did not apply, with the reason they gave.</summary>
    public List<(string Kind, string Why)> Skipped { get; } = [];
}

/// <summary>
/// The smallest host that satisfies the step contract: runs steps in order, keeps a cursor, stops
/// on a suspension and continues when a signal arrives.
/// <para>
/// It exists so the contract can be exercised — and so the contract can be shown to be genuinely
/// host-agnostic — without the workflow engine, a database or a background task. The real host is
/// the workflow engine; this one proves nothing in the steps depends on it.
/// </para>
/// </summary>
public class InMemoryAcquisitionHost(
    IAcquisitionStepRegistry registry,
    IServiceProvider serviceProvider,
    ILogger? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>
    /// Runs from the beginning, stopping at the first suspension.
    /// </summary>
    public Task<AcquisitionHostRun> StartAsync(AcquisitionRecipe recipe, AcquisitionWorkItem item,
        CancellationToken ct = default)
    {
        var run = new AcquisitionHostRun { Recipe = recipe, Item = item };

        return AdvanceAsync(run, null, ct);
    }

    /// <summary>
    /// Delivers a signal to the step the run is waiting on and carries on from there.
    /// </summary>
    /// <exception cref="InvalidOperationException">The run is not waiting for anything.</exception>
    public Task<AcquisitionHostRun> ResumeAsync(AcquisitionHostRun run, AcquisitionResumeSignal signal,
        CancellationToken ct = default)
    {
        if (run.State != AcquisitionRunState.Waiting)
        {
            throw new InvalidOperationException(
                $"The run is {run.State}, so there is nothing waiting for a signal.");
        }

        return AdvanceAsync(run, signal, ct);
    }

    private async Task<AcquisitionHostRun> AdvanceAsync(AcquisitionHostRun run,
        AcquisitionResumeSignal? signal, CancellationToken ct)
    {
        run.State = AcquisitionRunState.Completed;
        run.WaitReason = null;
        run.WaitPromptJson = null;

        while (run.StepIndex < run.Recipe.Steps.Count)
        {
            ct.ThrowIfCancellationRequested();

            var recipeStep = run.Recipe.Steps[run.StepIndex];

            if (!registry.TryGet(recipeStep.Kind, out var step))
            {
                run.State = AcquisitionRunState.Failed;
                run.FailureMessage = $"No step is registered for '{recipeStep.Kind}'.";

                return run;
            }

            var ctx = new AcquisitionStepContext(serviceProvider, _logger,
                (_, _) => Task.CompletedTask, run.Item.WorkingDirectory);

            // A signal belongs to the step that suspended, and only to its first execution after
            // the wait; everything downstream runs normally.
            var outcome = signal == null
                ? await step.ExecuteAsync(ctx, run.Item, ct)
                : await step.ResumeAsync(ctx, run.Item, signal, ct);

            signal = null;
            run.ExecutedKinds.Add(step.Kind);

            switch (outcome)
            {
                case AcquisitionStepOutcome.Continue c:
                    run.Item = c.Item;
                    run.StepIndex++;
                    break;

                case AcquisitionStepOutcome.Skip s:
                    // Not applicable is not a failure: the run moves on.
                    run.Item = s.Item;
                    run.Skipped.Add((step.Kind, s.Why));
                    run.StepIndex++;
                    break;

                case AcquisitionStepOutcome.Suspend sp:
                    // The cursor stays on this step: resuming calls it again, not the next one.
                    run.Item = sp.Item;
                    run.State = AcquisitionRunState.Waiting;
                    run.WaitReason = sp.Reason;
                    run.WaitPromptJson = sp.PromptJson;

                    return run;

                case AcquisitionStepOutcome.Fail f:
                    // The cursor stays too, so a retry re-runs the step that failed.
                    run.State = AcquisitionRunState.Failed;
                    run.FailureMessage = f.Message;
                    _logger.LogError(f.Error, "[Acquisition] Step {Kind} failed: {Message}", step.Kind,
                        f.Message);

                    return run;

                default:
                    throw new ArgumentOutOfRangeException(nameof(outcome), outcome,
                        "Unknown acquisition step outcome.");
            }
        }

        return run;
    }
}
