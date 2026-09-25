using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Media;
using Bakabase.Abstractions.Exceptions;
using CliWrap;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;

/// <summary>
/// <see cref="IMediaMerger"/> on FFmpeg stream copies (<see cref="FfMpegMergeArguments"/>).
/// </summary>
/// <remarks>
/// <para>Each operation checks free space first (≥ inputs × 1.1 on the output's volume), writes
/// <c>{output}.partial</c> and moves it over the output only when FFmpeg succeeded (exit 0, and no failed trailer
/// write or full disk in its error output). Disk full — found up front or
/// reported by FFmpeg — is a <see cref="DiskWriteException"/> (fatal; retrying with other streams only uses more
/// space); any other non-zero exit is a <see cref="MediaMergeException"/>.</para>
/// <para>Progress: FFmpeg writes <c>out_time_us</c> blocks to stdout (<c>-progress pipe:1</c>); the caller's
/// callback gets the position against <see cref="MediaMergeProgress.ExpectedDuration"/> at least every
/// <see cref="HeartbeatInterval"/> while the process runs, so a long copy on a slow disk never looks hung.</para>
/// <para>Cancellation kills the process (CliWrap) and deletes the partial output.</para>
/// </remarks>
public sealed class FfMpegMediaMerger : IMediaMerger
{
    /// <summary>
    /// Headroom over the inputs' size, in tenths (11 = × 1.1): the output is about their sum, plus container
    /// overhead.
    /// </summary>
    public const int FreeSpaceTenths = 11;

    private const int StandardErrorTailLength = 2000;
    private const int StandardErrorBufferLength = 64 * 1024;

    private readonly Func<CancellationToken, Task<string>> _getExecutable;
    private readonly IFfMpegProcessRunner _runner;
    private readonly Func<string, long?> _getAvailableFreeSpace;
    private readonly TimeProvider _time;
    private readonly ILogger<FfMpegMediaMerger> _logger;

    public FfMpegMediaMerger(FfMpegService ffMpeg, ILogger<FfMpegMediaMerger> logger)
        : this(ffMpeg.GetFfMpegExecutableAsync, new CliWrapFfMpegProcessRunner(), DiskSpace.TryGetAvailableFreeSpace,
            TimeProvider.System, logger)
    {
    }

    internal FfMpegMediaMerger(Func<CancellationToken, Task<string>> getExecutable, IFfMpegProcessRunner runner,
        Func<string, long?> getAvailableFreeSpace, TimeProvider time, ILogger<FfMpegMediaMerger> logger)
    {
        _getExecutable = getExecutable;
        _runner = runner;
        _getAvailableFreeSpace = getAvailableFreeSpace;
        _time = time;
        _logger = logger;
    }

    internal TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(2);

    public Task MuxAsync(MediaMuxRequest request, MediaMergeProgress? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var inputs = request.AudioPath == null ? new[] {request.VideoPath} : new[] {request.VideoPath, request.AudioPath};
        return RunAsync("mux", inputs, request.OutputPath, partial => FfMpegMergeArguments.Mux(request, partial),
            progress, ct);
    }

    public Task RemuxAsync(string inputPath, string outputPath, MediaMergeProgress? progress, CancellationToken ct) =>
        RunAsync("remux", [inputPath], outputPath, partial => FfMpegMergeArguments.Remux(inputPath, partial),
            progress, ct);

    public async Task ConcatAsync(IReadOnlyList<string> inputPaths, string outputPath, MediaMergeProgress? progress,
        CancellationToken ct)
    {
        if (inputPaths == null || inputPaths.Count == 0)
        {
            throw new ArgumentException("At least one input is required.", nameof(inputPaths));
        }

        var listFile = Path.GetFullPath(outputPath) + ".concat.txt";
        try
        {
            await DiskWriteException.GuardAsync(listFile, () => File.WriteAllTextAsync(listFile,
                FfMpegMergeArguments.ConcatList(inputPaths.Select(Path.GetFullPath)), new UTF8Encoding(false), ct));

            await RunAsync("concat", inputPaths, outputPath,
                partial => FfMpegMergeArguments.Concat(listFile, partial), progress, ct);
        }
        finally
        {
            TryDelete(listFile);
        }
    }

    private async Task RunAsync(string operation, IReadOnlyList<string> inputs, string outputPath,
        Func<string, IReadOnlyList<string>> buildArguments, MediaMergeProgress? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var output = Path.GetFullPath(outputPath);
        var partial = output + ".partial";
        EnsureFreeSpace(inputs, output);

        var executable = await _getExecutable(ct);
        var arguments = buildArguments(partial);
        var position = new FfMpegProgressParser();
        var stderr = new BoundedText(StandardErrorBufferLength);

        _logger.LogDebug("ffmpeg {Operation}: {Inputs} → {Output}", operation,
            string.Join(", ", inputs.Select(i => Path.GetFileName(i))), Path.GetFileName(output));

        int exitCode;
        using (var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            var heartbeat = progress == null
                ? Task.CompletedTask
                : HeartbeatAsync(progress, position, heartbeatStop.Token);
            try
            {
                exitCode = await _runner.RunAsync(executable, arguments, position.OnLine, stderr.AppendLine, ct);
            }
            catch
            {
                TryDelete(partial);
                throw;
            }
            finally
            {
                heartbeatStop.Cancel();
                await heartbeat;
            }
        }

        var tail = stderr.Tail(StandardErrorTailLength);
        if (exitCode != 0 || FailedDespiteExitCodeZero(tail))
        {
            TryDelete(partial);
            var lastLine = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            var failure = new MediaMergeException(
                $"ffmpeg ({operation}) " +
                (exitCode != 0 ? $"exited with code {exitCode}" : "could not finish its output (exit code 0)") +
                (string.IsNullOrEmpty(lastLine) ? "." : $": {Truncate(lastLine, 300)}"),
                exitCode, tail);
            _logger.LogWarning("ffmpeg {Operation} of {Output} failed with exit code {ExitCode}: {StandardError}",
                operation, Path.GetFileName(output), exitCode, Truncate(tail, 500));
            if (DiskWriteException.IsDiskFullMessage(tail))
            {
                throw new DiskWriteException(output, true, failure);
            }

            throw failure;
        }

        try
        {
            File.Move(partial, output, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(partial);
            throw DiskWriteException.From(output, e);
        }

        if (progress != null)
        {
            await ReportAsync(progress, 1d);
        }
    }

    /// <summary>
    /// Older FFmpeg builds log a failed trailer write (the MP4 index) and still exit 0, leaving a file no player
    /// can open. Only those two signs count: other error-level lines are often harmless.
    /// </summary>
    private static bool FailedDespiteExitCodeZero(string standardErrorTail) =>
        DiskWriteException.IsDiskFullMessage(standardErrorTail) ||
        standardErrorTail.Contains("Error writing trailer", StringComparison.OrdinalIgnoreCase);

    private void EnsureFreeSpace(IEnumerable<string> inputs, string output)
    {
        long required = 0;
        foreach (var input in inputs)
        {
            var info = new FileInfo(input);
            if (info.Exists)
            {
                required += info.Length;
            }
        }

        // Integer arithmetic: × 1.1 in floating point turns 11 000 into 12 100.000000000002.
        required = (required * FreeSpaceTenths + 9) / 10;
        var available = _getAvailableFreeSpace(output);
        if (available is { } free && free < required)
        {
            _logger.LogWarning(
                "Not enough free space to write {Output}: {Available} bytes available, {Required} bytes required.",
                Path.GetFileName(output), free, required);
            throw new DiskWriteException(output, true);
        }
    }

    private async Task HeartbeatAsync(MediaMergeProgress progress, FfMpegProgressParser position,
        CancellationToken stop)
    {
        try
        {
            using var timer = new PeriodicTimer(HeartbeatInterval, _time);
            do
            {
                await ReportAsync(progress, position.Fraction(progress.ExpectedDuration));
            } while (await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    private async Task ReportAsync(MediaMergeProgress progress, double? fraction)
    {
        try
        {
            await progress.Report(fraction);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A progress sink failing must not fail (or orphan) the merge.
            _logger.LogWarning(e, "A merge progress callback failed.");
        }
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not delete {File}: {Error}", Path.GetFileName(path), e.Message);
        }
    }

    /// <summary>Keeps the last <c>capacity</c> characters of a process's error output.</summary>
    private sealed class BoundedText(int capacity)
    {
        private readonly StringBuilder _text = new();
        private readonly object _lock = new();

        public void AppendLine(string line)
        {
            lock (_lock)
            {
                _text.Append(line).Append('\n');
                if (_text.Length > capacity)
                {
                    _text.Remove(0, _text.Length - capacity);
                }
            }
        }

        public string Tail(int length)
        {
            lock (_lock)
            {
                var text = _text.ToString().TrimEnd();
                return text.Length <= length ? text : text[^length..];
            }
        }
    }
}

/// <summary>
/// Reads FFmpeg's <c>-progress</c> output (<c>key=value</c> lines). <c>out_time_us</c> is the position in
/// microseconds; <c>out_time_ms</c> is microseconds too (a historical misnomer), used when the former is missing.
/// </summary>
internal sealed class FfMpegProgressParser
{
    private long _positionMicroseconds = -1;

    public void OnLine(string line)
    {
        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return;
        }

        var key = line[..separator].Trim();
        if (key is not ("out_time_us" or "out_time_ms"))
        {
            return;
        }

        if (long.TryParse(line[(separator + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var microseconds) && microseconds >= 0)
        {
            Interlocked.Exchange(ref _positionMicroseconds, microseconds);
        }
    }

    public TimeSpan? Position
    {
        get
        {
            var value = Interlocked.Read(ref _positionMicroseconds);
            return value < 0 ? null : TimeSpan.FromTicks(value * 10);
        }
    }

    /// <summary>Position / duration in [0, 1], or <c>null</c> when either is unknown.</summary>
    public double? Fraction(TimeSpan? duration) =>
        Position is { } position && duration is { TotalMicroseconds: > 0 } d
            ? Math.Clamp(position.TotalMicroseconds / d.TotalMicroseconds, 0d, 1d)
            : null;
}

/// <summary>Runs FFmpeg; a seam so tests (CI has no FFmpeg) can stand in for the process.</summary>
internal interface IFfMpegProcessRunner
{
    /// <returns>The exit code.</returns>
    Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, Action<string> onStandardOutputLine,
        Action<string> onStandardErrorLine, CancellationToken ct);
}

internal sealed class CliWrapFfMpegProcessRunner : IFfMpegProcessRunner
{
    public async Task<int> RunAsync(string executable, IReadOnlyList<string> arguments,
        Action<string> onStandardOutputLine, Action<string> onStandardErrorLine, CancellationToken ct)
    {
        var result = await Cli.Wrap(executable)
            .WithArguments(arguments, true)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(onStandardOutputLine, Encoding.UTF8))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(onStandardErrorLine, Encoding.UTF8))
            .ExecuteAsync(ct);
        return result.ExitCode;
    }
}

/// <summary>Free space of the volume a path is on.</summary>
internal static class DiskSpace
{
    /// <summary>
    /// The available free space of the mounted volume holding <paramref name="path"/> (the longest mount point
    /// that is a prefix of it — on Linux/macOS <c>Path.GetPathRoot</c> is always <c>/</c>), or <c>null</c> when it
    /// cannot be told (then no check is made).
    /// </summary>
    public static long? TryGetAvailableFreeSpace(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            DriveInfo? best = null;
            foreach (var drive in DriveInfo.GetDrives())
            {
                string root;
                try
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }

                    root = drive.RootDirectory.FullName;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
                var matches = full.StartsWith(rootWithSeparator, comparison) ||
                              string.Equals(full, root, comparison);
                if (matches && (best == null || root.Length > best.RootDirectory.FullName.Length))
                {
                    best = drive;
                }
            }

            return best?.AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                      or NotSupportedException)
        {
            return null;
        }
    }
}
