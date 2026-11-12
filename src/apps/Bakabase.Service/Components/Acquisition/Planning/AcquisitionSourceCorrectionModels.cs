using System;
using System.Collections.Generic;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionSourceCorrectionHistory(string CorrectionId, string? PreviousAccessCode,
    string? NewAccessCode, DateTime AppliedAt);
public sealed record AcquisitionSourceCorrectionRow(string TargetId, string SourceId, string ExpectedSourceIdentity,
    string Url, string ProviderId, string? AccessCode, bool CanCorrect, string? ReasonCode,
    IReadOnlyList<AcquisitionSourceCorrectionHistory> History, CloudSource CurrentSource);
public sealed record AcquisitionSourceCorrections(int TaskId, int ExpectedRevision, int? ExpectedRunId,
    string? CheckpointVersion, bool CanCorrect, string? ReasonCode,
    IReadOnlyList<AcquisitionSourceCorrectionRow> Sources);
public sealed record AcquisitionSourceCorrectionInput(int ExpectedRevision, int ExpectedRunId,
    string CheckpointVersion, string CorrectionId, string TargetId, string SourceId,
    string ExpectedSourceIdentity, string? NewAccessCode);
public sealed record AcquisitionSourceCorrectionResult(int TaskId, int WorkflowRunId, bool Accepted);
