using System;
using System.Collections.Generic;

namespace Bakabase.Service.Models.View;

public record AppStorageUsageViewModel
{
    public string RootPath { get; init; } = null!;
    /// <summary>Sum of logical file lengths, not filesystem allocated blocks. Links are excluded.</summary>
    public long TotalBytes { get; init; }
    public long FileCount { get; init; }
    public DateTimeOffset ScannedAt { get; init; }
    public bool FromCache { get; init; }
    public bool Partial { get; init; }
    public long SkippedSymbolicLinks { get; init; }
    public IReadOnlyList<AppStorageUsageCategoryViewModel> Categories { get; init; } = [];
    /// <summary>Bounded sample of inaccessible or concurrently removed paths.</summary>
    public IReadOnlyList<AppStorageUsageErrorViewModel> Errors { get; init; } = [];
    public long ErrorCount { get; init; }
}

public record AppStorageUsageCategoryViewModel
{
    public string Id { get; init; } = null!;
    public long Bytes { get; init; }
    public long FileCount { get; init; }
    /// <summary>Dedicated category parent, when one exists. Resolving it never creates it.</summary>
    public string? Path { get; init; }
    public bool Partial { get; init; }
}

public record AppStorageUsageErrorViewModel
{
    public string Path { get; init; } = null!;
    public string Message { get; init; } = null!;
}
