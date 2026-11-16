using System;
using System.Collections.Generic;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionStageDirectoryChoice(string Directory, bool MoveFiles = true);
public sealed record AcquisitionStageDirectoryPartChange(string PartId,
    AcquisitionStageDirectoryChoice? Download = null, AcquisitionStageDirectoryChoice? Processing = null,
    AcquisitionStageDirectoryChoice? Delivery = null);
public sealed record AcquisitionStageDirectoryChangeInput(int ExpectedRevision,
    IReadOnlyList<AcquisitionStageDirectoryPartChange> Parts);
public sealed record AcquisitionStageDirectoryPartSnapshot(string PartId, string? Title,
    string? DownloadDirectory, string? ProcessingDirectory, string? DeliveryDirectory);
public sealed record AcquisitionStageDirectorySnapshot(int TaskId, int ExpectedRevision,
    IReadOnlyList<AcquisitionStageDirectoryPartSnapshot> Parts, bool CanChange,
    IReadOnlyList<string> Blockers, AcquisitionStageDirectoryChangeInput? PendingChange = null);
public sealed record AcquisitionStageDirectoryChangeItem(string PartId, string Stage, string? SourceDirectory,
    string Directory, bool MoveFiles, bool Changed, long FileCount, long TotalBytes, bool Eligible, string? Code = null);
public sealed record AcquisitionStageDirectoryChangePreview(int TaskId, int ExpectedRevision,
    IReadOnlyList<AcquisitionStageDirectoryChangeItem> Changes, bool CanApply);
public sealed record AcquisitionStageDirectoryChangeResult(int TaskId, int Revision,
    AcquisitionStageDirectories Directories, IReadOnlyList<AcquisitionStageDirectoryChangeItem> Changes);

// Durable operation intent, stored in the task's existing builder state. It blocks execution
// until the same request is reconciled, without editing immutable run or artifact records.
internal sealed record AcquisitionStageDirectoryChangeJournal(string Id, int Revision,
    AcquisitionStageDirectoryChangeInput Input, AcquisitionStageDirectories Directories,
    List<AcquisitionStageDirectoryMove> Moves, DateTime StartedAt);
internal sealed record AcquisitionStageDirectoryMove(string PartId, string Stage, string? SourceDirectory,
    string Directory, bool MoveFiles, List<AcquisitionStageDirectoryMoveFile> Files,
    IReadOnlyList<string> EmptyDirectories, bool Completed = false);
internal sealed record AcquisitionStageDirectoryMoveFile(string RelativePath, long Length, long LastWriteUtcTicks,
    string? CopiedHash = null, bool Completed = false);
