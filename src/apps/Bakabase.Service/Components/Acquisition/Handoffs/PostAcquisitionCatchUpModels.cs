using System.Collections.Generic;

namespace Bakabase.Service.Components.Acquisition.Handoffs;

public sealed record PostAcquisitionCatchUpPrepareInput(IReadOnlyList<int> TaskIds);

/// <summary>Exact saved-result selection, not permission to reparse or purchase content.</summary>
public sealed record PostAcquisitionCatchUpSelection(int TaskId, int Revision, int? WorkflowRunId,
    string ResultsHash);

public sealed record PostAcquisitionCatchUpConfirmInput(IReadOnlyList<PostAcquisitionCatchUpSelection> Tasks);

public sealed record PostAcquisitionCatchUpItem(int TaskId, string? Title, string Status, string? Code,
    PostAcquisitionCatchUpSelection? Selection, string? HandoffId, IReadOnlyList<int> AcquisitionTaskIds,
    string? Link = null);

public sealed record PostAcquisitionCatchUpPreview(int RequestedCount, int EligibleCount, int ExistingCount,
    int RejectedCount, IReadOnlyList<PostAcquisitionCatchUpItem> Items);

public sealed record PostAcquisitionFilteredCatchUpPreview(int MatchedCount, int MaxTasks, bool LimitExceeded,
    PostAcquisitionCatchUpPreview? Preview);

public sealed record PostAcquisitionCatchUpResult(int RequestedCount, int AcceptedCount, int ExistingCount,
    int RejectedCount, IReadOnlyList<PostAcquisitionCatchUpItem> Items);
