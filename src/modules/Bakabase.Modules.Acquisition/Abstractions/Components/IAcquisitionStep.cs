using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Acquisition.Abstractions.Components;

/// <summary>
/// How a step ended, and therefore what the host does with the cursor.
/// </summary>
public abstract record AcquisitionStepOutcome
{
    private AcquisitionStepOutcome()
    {
    }

    /// <summary>Done; move to the next step with this item.</summary>
    public sealed record Continue(AcquisitionWorkItem Item) : AcquisitionStepOutcome;

    /// <summary>
    /// Waiting for something from outside — a file, a password, a decision. The host records the
    /// reason and the prompt, ends the run's task, and calls back into
    /// <see cref="IAcquisitionStep.ResumeAsync"/> when the signal arrives.
    /// </summary>
    /// <param name="PromptJson">What the interface should ask. Shape is the step's own business.</param>
    public sealed record Suspend(
        AcquisitionWaitReason Reason,
        string? PromptJson,
        AcquisitionWorkItem Item) : AcquisitionStepOutcome;

    /// <summary>
    /// This step does not apply — there was no archive to unpack, nothing to rename. Not a
    /// failure: the run carries on with the next step.
    /// </summary>
    public sealed record Skip(string Why, AcquisitionWorkItem Item) : AcquisitionStepOutcome;

    /// <summary>The run stops here.</summary>
    public sealed record Fail(string Message, Exception? Error = null) : AcquisitionStepOutcome;
}

/// <summary>
/// What arrived from outside while a step was suspended. The payload is the step's own shape — the
/// file that was claimed, the password that was typed, the link that was picked — so this is only
/// a typed envelope over it.
/// </summary>
/// <param name="Reason">The wait it answers, so a step can tell two of its own waits apart.</param>
/// <param name="PayloadJson">The answer, in whatever shape the step asked for.</param>
public record AcquisitionResumeSignal(AcquisitionWaitReason Reason, string? PayloadJson);

/// <summary>
/// What a step is given besides the work item: the services it needs, somewhere to log, a way to
/// report progress, and the directory it may write in.
/// </summary>
/// <param name="ServiceProvider">Scoped — a step resolves what it needs and does not hold it.</param>
/// <param name="Logger">Written to the run's log.</param>
/// <param name="ReportProgress">Percentage within this step (0-100) and an optional description.</param>
/// <param name="WorkingDirectory">This run's own directory; a step must not write outside it.</param>
/// <param name="ConfigJson">
/// This step's configuration as the recipe stored it, in the shape of <see cref="IAcquisitionStep.ConfigType"/>.
/// </param>
public record AcquisitionStepContext(
    IServiceProvider ServiceProvider,
    ILogger Logger,
    Func<int, string?, Task> ReportProgress,
    string WorkingDirectory,
    string? ConfigJson = null)
{
    /// <summary>
    /// The step's own configuration, or null when the recipe left it out. Malformed JSON reads as
    /// null too: a step that cannot read its config should fall back to its defaults rather than
    /// take the whole run down.
    /// </summary>
    public T? GetConfig<T>() where T : class
    {
        if (string.IsNullOrWhiteSpace(ConfigJson)) return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(ConfigJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        }
        catch (System.Text.Json.JsonException)
        {
            Logger.LogWarning("An acquisition step was given configuration it could not read: {Json}", ConfigJson);

            return null;
        }
    }
}

/// <summary>
/// One pluggable stage of getting a resource. Stateless and registered as a singleton, discovered
/// by enumerating the DI container the way enhancers and subscription providers are.
/// <para>
/// Steps must be idempotent. The host re-runs the step at the cursor after a restart, so a fetch
/// that already has its file must notice and skip, and a placement whose destination already
/// matches must do nothing.
/// </para>
/// <para>
/// Deliberately free of any workflow type: the host provides the cursor, persistence, suspension,
/// concurrency and progress, and could be replaced without touching a single step.
/// </para>
/// </summary>
public interface IAcquisitionStep
{
    /// <summary>Identifier, following the same grammar as workflow activity kinds — <c>acquisition.unpack</c>.</summary>
    string Kind { get; }

    string DisplayName { get; }

    /// <summary>The shape of this step's configuration JSON, which the editor renders a form for. Null when it takes none.</summary>
    Type? ConfigType { get; }

    Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        CancellationToken ct);

    /// <summary>
    /// Continues from a suspension. Only steps that suspend implement it; the rest never see a
    /// signal, so the default says so loudly rather than pretending to handle one.
    /// </summary>
    Task<AcquisitionStepOutcome> ResumeAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        AcquisitionResumeSignal signal, CancellationToken ct) =>
        throw new NotSupportedException($"Step '{Kind}' does not suspend, so it cannot be resumed.");
}
