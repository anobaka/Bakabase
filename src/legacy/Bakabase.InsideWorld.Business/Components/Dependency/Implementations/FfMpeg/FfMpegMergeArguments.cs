using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Components.Media;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;

/// <summary>
/// FFmpeg argument lists for <see cref="FfMpegMediaMerger"/>: stream copies only (no re-encoding), always MP4.
/// One list element per argv entry — passed to the process without a shell, so paths need no quoting.
/// </summary>
public static class FfMpegMergeArguments
{
    /// <summary>
    /// Quiet, non-interactive, overwrite, and machine-readable progress on stdout (<c>-progress pipe:1</c>, with the
    /// human-readable stats line off) so the merger can report progress and keep a task watchdog alive.
    /// </summary>
    public static readonly IReadOnlyList<string> Common =
        ["-hide_banner", "-nostdin", "-loglevel", "error", "-nostats", "-progress", "pipe:1", "-y"];

    /// <summary>
    /// <c>-i video [-i audio] -map 0:v:0 [-map 1:a:0] -c copy [-tag:v hvc1] [-strict -2] -f mp4 output</c>.
    /// </summary>
    public static IReadOnlyList<string> Mux(MediaMuxRequest request, string output)
    {
        var args = new List<string>(Common) {"-i", request.VideoPath};
        if (request.AudioPath != null)
        {
            args.AddRange(["-i", request.AudioPath]);
        }

        args.AddRange(["-map", "0:v:0"]);
        if (request.AudioPath != null)
        {
            args.AddRange(["-map", "1:a:0"]);
        }

        args.AddRange(["-c", "copy"]);
        if (request.TagHevcAsHvc1)
        {
            args.AddRange(["-tag:v", "hvc1"]);
        }

        // Only audio can need it (FLAC / E-AC-3 in MP4); a video-only mux never does.
        if (request.AllowExperimentalCodecs && request.AudioPath != null)
        {
            args.AddRange(["-strict", "-2"]);
        }

        args.AddRange(["-f", "mp4", output]);
        return args;
    }

    /// <summary><c>-i input -map 0:v? -map 0:a? -c copy -f mp4 output</c> (FLV/MP4 → MP4).</summary>
    public static IReadOnlyList<string> Remux(string input, string output) =>
        [..Common, "-i", input, "-map", "0:v?", "-map", "0:a?", "-c", "copy", "-f", "mp4", output];

    /// <summary>
    /// <c>-f concat -safe 0 -i listFile -map 0:v? -map 0:a? -c copy -f mp4 output</c> (<c>-safe 0</c>: the list
    /// holds absolute paths).
    /// </summary>
    public static IReadOnlyList<string> Concat(string listFile, string output) =>
    [
        ..Common, "-f", "concat", "-safe", "0", "-i", listFile, "-map", "0:v?", "-map", "0:a?", "-c", "copy",
        "-f", "mp4", output
    ];

    /// <summary>
    /// An <c>ffconcat</c> list of <paramref name="absolutePaths"/> in order. Inside single quotes everything is
    /// literal except the quote itself, which is written as <c>'\''</c>.
    /// </summary>
    public static string ConcatList(IEnumerable<string> absolutePaths) =>
        "ffconcat version 1.0\n" +
        string.Concat(absolutePaths.Select(p => "file '" + p.Replace("'", "'\\''") + "'\n"));
}
