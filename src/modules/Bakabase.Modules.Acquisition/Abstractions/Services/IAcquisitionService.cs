using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Abstractions.Services;

/// <summary>
/// What the user asked for when they said "get me this". One call covers every route: which recipe
/// runs is decided by the lead, and everything after that is the recipe's business.
/// </summary>
public interface IAcquisitionService
{
    /// <summary>
    /// Starts getting a resource.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The resource does not exist, already has local files, is already being acquired, or no recipe
    /// is available for the lead.
    /// </exception>
    Task<AcquisitionTask> CreateAsync(int resourceId, AcquisitionLeadKind leadKind, string leadValue,
        int? leadId = null, int? recipeDefinitionId = null, int? collectionId = null,
        CancellationToken ct = default);

    Task<List<AcquisitionTask>> SearchAsync(AcquisitionStatus? status = null, int? resourceId = null,
        CancellationToken ct = default);

    Task<AcquisitionTask?> GetAsync(int taskId, CancellationToken ct = default);

    /// <summary>Answers whatever the task's current step is waiting for.</summary>
    Task ResumeAsync(int taskId, string signalJson, CancellationToken ct = default);

    /// <summary>
    /// Runs a failed or cancelled task again from where it stopped. The step at the cursor re-runs,
    /// which is why steps must be idempotent.
    /// </summary>
    Task<AcquisitionTask> RetryAsync(int taskId, CancellationToken ct = default);

    Task CancelAsync(int taskId, CancellationToken ct = default);

    /// <summary>
    /// Mirrors a run's state onto the task row. Called by the step adapter after every step; not
    /// meant for anything else.
    /// </summary>
    Task SyncFromRunAsync(int taskId, long runId, AcquisitionStatus status,
        AcquisitionWaitReason? waitReason, string? targetDirectory,
        IReadOnlyList<PurchaseRecord>? purchases, string? error, CancellationToken ct = default);

    /// <summary>The recipes available to start a task with, built-in and user-made alike.</summary>
    Task<List<AcquisitionRecipeSummary>> GetRecipesAsync(CancellationToken ct = default);
}

/// <summary>A recipe as the picker shows it.</summary>
public record AcquisitionRecipeSummary(int DefinitionId, string Name, bool IsBuiltin, List<string> StepKinds);
