namespace Bakabase.Abstractions.Components.Media;

/// <summary>
/// Joins already-downloaded media files into one MP4 without re-encoding. Implemented in the legacy layer on
/// top of FFmpeg; modules depend on this interface only, so tests can replace it (CI has no FFmpeg).
/// </summary>
/// <remarks>
/// Every method writes <c>{output}.partial</c> first and moves it over the output only on success, so a failed
/// or cancelled run never leaves a truncated output behind. A failure of the tool is a
/// <see cref="MediaMergeException"/>; running out of disk space (checked up front, or reported by the tool) is a
/// <see cref="Exceptions.DiskWriteException"/> instead, which callers must not answer with another attempt.
/// </remarks>
public interface IMediaMerger
{
    /// <summary>Copies video (+ optional audio) into one MP4 without re-encoding.</summary>
    Task MuxAsync(MediaMuxRequest request, MediaMergeProgress? progress, CancellationToken ct);

    /// <summary>Copies every video/audio stream of one file into MP4 (FLV/MP4 → MP4) without re-encoding.</summary>
    Task RemuxAsync(string inputPath, string outputPath, MediaMergeProgress? progress, CancellationToken ct);

    /// <summary>Joins same-codec segments, in the given order, into one MP4 without re-encoding.</summary>
    Task ConcatAsync(IReadOnlyList<string> inputPaths, string outputPath, MediaMergeProgress? progress,
        CancellationToken ct);
}

public sealed record MediaMuxRequest(string VideoPath, string? AudioPath, string OutputPath)
{
    /// <summary>Writes HEVC as <c>hvc1</c> (players on Apple platforms refuse <c>hev1</c> in MP4).</summary>
    public bool TagHevcAsHvc1 { get; init; }

    /// <summary>Allows codecs MP4 muxers treat as experimental (FLAC, E-AC-3).</summary>
    public bool AllowExperimentalCodecs { get; init; }
}

/// <summary>
/// Progress of one merge. <paramref name="Report"/> receives the fraction done (0–1) when
/// <paramref name="ExpectedDuration"/> is known and the tool has reported a position, otherwise <c>null</c>
/// ("still working"). It is called at least every few seconds while the tool runs — a merge of a large file can
/// take longer than a task watchdog allows between two signs of life — and never concurrently with itself.
/// </summary>
public sealed record MediaMergeProgress(Func<double?, Task> Report, TimeSpan? ExpectedDuration = null);

/// <summary>The merge tool failed (non-zero exit). The output file was not written.</summary>
public sealed class MediaMergeException(string message, int exitCode, string? standardErrorTail)
    : Exception(message)
{
    public int ExitCode { get; } = exitCode;

    /// <summary>The end of the tool's error output (local file paths only; capped).</summary>
    public string? StandardErrorTail { get; } = standardErrorTail;
}
