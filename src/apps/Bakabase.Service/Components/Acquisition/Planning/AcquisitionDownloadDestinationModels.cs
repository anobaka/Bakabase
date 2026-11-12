using System;
using System.Collections.Generic;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionDownloadDestinationPreviewInput(IReadOnlyList<int> TaskIds, string TargetRootDirectory);
public sealed record AcquisitionDownloadDestinationApplyInput(string PreviewToken, IReadOnlyList<int> TaskIds);
public sealed record AcquisitionDownloadDestinationItem(int? DownloadTaskId, string? OldDirectory, string? NewDirectory,
    bool CanApply, string ReasonCode);
public sealed record AcquisitionDownloadDestinationRow(int TaskId, string? Title, bool CanApply, string ReasonCode,
    IReadOnlyList<AcquisitionDownloadDestinationItem> Downloads);
public sealed record AcquisitionDownloadDestinationPreview(string PreviewToken, DateTimeOffset ExpiresAt, int TaskCount,
    int DownloadCount, IReadOnlyList<AcquisitionDownloadDestinationRow> Rows);
public sealed record AcquisitionDownloadDestinationSkip(int TaskId, string ReasonCode);
public sealed record AcquisitionDownloadDestinationResult(IReadOnlyList<int> AppliedTaskIds,
    IReadOnlyList<AcquisitionDownloadDestinationSkip> Skipped, int AppliedDownloadCount);
