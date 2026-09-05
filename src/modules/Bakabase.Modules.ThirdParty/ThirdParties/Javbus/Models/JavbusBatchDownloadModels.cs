using System;
using System.Collections.Generic;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Javbus.Models;

/// <summary>Why a code did or didn't produce a magnet.</summary>
public enum JavbusBatchItemStatus
{
    Succeeded = 1,

    /// <summary>Javbus has no page for this code (it answers with a soft-200 landing page).</summary>
    NotIndexed = 2,

    /// <summary>The page exists but lists no magnets.</summary>
    NoMagnet = 3,

    /// <summary>The request itself failed — network, Cloudflare, a rate limit; see the message.</summary>
    Failed = 4
}

/// <summary>The knobs one batch run uses. Resolved from user options by the caller.</summary>
public record JavbusBatchDownloadSettings
{
    public int Concurrency { get; init; } = 2;

    public int DelayMs { get; init; } = 600;

    /// <summary>Fraction (0-0.9): how far below the largest candidate still counts as the same quality tier.</summary>
    public decimal SizeTolerance { get; init; } = 0.3m;

    /// <summary>Where covers land. Null or empty means magnets only.</summary>
    public string? CoverDirectory { get; init; }
}

/// <summary>One row of the batch result table.</summary>
public record JavbusBatchDownloadItem
{
    public string Code { get; init; } = string.Empty;

    public JavbusBatchItemStatus Status { get; init; }

    /// <summary>Set when <see cref="Status"/> is <see cref="JavbusBatchItemStatus.Failed"/>.</summary>
    public string? Error { get; init; }

    public string? Title { get; init; }

    public string? DetailUrl { get; init; }

    public string? CoverUrl { get; init; }

    /// <summary>Where the cover was written, when covers are enabled and the download worked.</summary>
    public string? CoverPath { get; init; }

    /// <summary>A cover that failed to download doesn't fail the row — the magnet is the point.</summary>
    public string? CoverError { get; init; }

    /// <summary>How many magnets the code had before selection, so a lonely pick is visible as such.</summary>
    public int CandidateCount { get; init; }

    public JavbusMagnet? Magnet { get; init; }
}

/// <summary>Everything the tool page polls for while a batch runs.</summary>
public record JavbusBatchDownloadState
{
    public bool IsRunning { get; init; }

    public int Total { get; init; }

    public int Done { get; init; }

    public string? CoverDirectory { get; init; }

    public DateTime? StartedAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    /// <summary>In submission order, so the table doesn't reshuffle as workers finish out of order.</summary>
    public List<JavbusBatchDownloadItem> Items { get; init; } = [];
}
