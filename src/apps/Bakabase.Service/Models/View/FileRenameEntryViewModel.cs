using System;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.View;

/// <summary>
/// One row of a run's rename plan, as the RenamePlanPanel consumes it — both the interactive
/// confirm surface and the read-only run-detail view render this same shape (owner decision,
/// capability map §9·决定 2).
/// </summary>
public record FileRenameEntryViewModel
{
    public int Id { get; init; }
    public int RunId { get; init; }
    public int Seq { get; init; }
    public string Path { get; init; } = null!;
    public string From { get; init; } = null!;
    public string To { get; init; } = "";
    public FileRenameStatus Status { get; init; }
    public string? Error { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? AppliedAt { get; init; }

    public static FileRenameEntryViewModel FromDb(FileRenameEntry e) => new()
    {
        Id = e.Id,
        RunId = e.RunId,
        Seq = e.Seq,
        Path = e.Path,
        From = e.From,
        To = e.To,
        Status = e.Status,
        Error = e.Error,
        CreatedAt = e.CreatedAt,
        AppliedAt = e.AppliedAt
    };
}
