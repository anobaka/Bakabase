using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Media;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

/// <summary>
/// <see cref="FfMpegMediaMerger"/> with a stand-in for the FFmpeg process (CI has none): argument lists, the
/// partial-then-move contract, failure classes (tool error vs. full disk), progress and cancellation.
/// </summary>
[TestClass]
public class FfMpegMediaMergerTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ffmpeg-merger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private string File(string name, int size = 1000)
    {
        var path = Path.Combine(_dir, name);
        System.IO.File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    private FfMpegMediaMerger Create(StubRunner runner, long? freeSpace = long.MaxValue) =>
        new(_ => Task.FromResult("/opt/ffmpeg/ffmpeg"), runner, _ => freeSpace, TimeProvider.System,
            NullLogger<FfMpegMediaMerger>.Instance)
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
        };

    [TestMethod]
    public async Task Mux_RunsTheArgumentList_WritesPartialThenMoves()
    {
        var video = File("v-80-c12.m4s");
        var audio = File("a-30280.m4s");
        var output = Path.Combine(_dir, "merged.mp4");
        var runner = new StubRunner();
        var request = new MediaMuxRequest(video, audio, output) {TagHevcAsHvc1 = true};

        await Create(runner).MuxAsync(request, null, CancellationToken.None);

        var call = runner.Calls.Single();
        Assert.AreEqual("/opt/ffmpeg/ffmpeg", call.Executable);
        CollectionAssert.AreEqual(FfMpegMergeArguments.Mux(request, output + ".partial").ToArray(),
            call.Arguments.ToArray());
        Assert.IsTrue(System.IO.File.Exists(output));
        Assert.IsFalse(System.IO.File.Exists(output + ".partial"));
    }

    [TestMethod]
    public async Task Remux_ReplacesAnExistingOutput()
    {
        var input = File("seg-1.flv");
        var output = File("merged.mp4", 5);
        var runner = new StubRunner {OutputBytes = 42};

        await Create(runner).RemuxAsync(input, output, null, CancellationToken.None);

        CollectionAssert.AreEqual(FfMpegMergeArguments.Remux(input, output + ".partial").ToArray(),
            runner.Calls.Single().Arguments.ToArray());
        Assert.AreEqual(42, new FileInfo(output).Length);
    }

    [TestMethod]
    public async Task Concat_WritesTheListInOrder_AndDeletesIt()
    {
        var segments = new[] {File("seg-1.flv"), File("seg-2.flv"), File("seg-10.flv")};
        var output = Path.Combine(_dir, "merged.mp4");
        var listFile = output + ".concat.txt";
        string? listSeenByFfmpeg = null;
        var runner = new StubRunner {OnRun = _ => listSeenByFfmpeg = System.IO.File.ReadAllText(listFile)};

        await Create(runner).ConcatAsync(segments, output, null, CancellationToken.None);

        Assert.AreEqual(FfMpegMergeArguments.ConcatList(segments), listSeenByFfmpeg);
        CollectionAssert.AreEqual(FfMpegMergeArguments.Concat(listFile, output + ".partial").ToArray(),
            runner.Calls.Single().Arguments.ToArray());
        Assert.IsFalse(System.IO.File.Exists(listFile));
        Assert.IsTrue(System.IO.File.Exists(output));
    }

    [TestMethod]
    public async Task Concat_NoInputs_Throws()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            Create(new StubRunner()).ConcatAsync([], Path.Combine(_dir, "o.mp4"), null, CancellationToken.None));
    }

    [TestMethod]
    public async Task NonZeroExit_MediaMergeException_NoOutput()
    {
        var output = Path.Combine(_dir, "merged.mp4");
        var runner = new StubRunner
        {
            ExitCode = 1,
            StandardError = ["[mp4 @ 0x0] Could not find tag for codec flac in stream #1", "Conversion failed!"],
        };

        var e = await Assert.ThrowsExceptionAsync<MediaMergeException>(() => Create(runner)
            .MuxAsync(new MediaMuxRequest(File("v.m4s"), File("a.m4s"), output), null, CancellationToken.None));

        Assert.AreEqual(1, e.ExitCode);
        StringAssert.Contains(e.StandardErrorTail, "Could not find tag for codec flac");
        StringAssert.Contains(e.Message, "Conversion failed!");
        Assert.IsFalse(System.IO.File.Exists(output));
        Assert.IsFalse(System.IO.File.Exists(output + ".partial"), "the partial output is removed");
    }

    [TestMethod]
    public async Task NoSpaceLeft_IsADiskError_NotAMergeFailure()
    {
        var runner = new StubRunner
        {
            ExitCode = 1,
            StandardError = ["av_interleaved_write_frame(): No space left on device", "Conversion failed!"],
        };

        var e = await Assert.ThrowsExceptionAsync<DiskWriteException>(() => Create(runner)
            .MuxAsync(new MediaMuxRequest(File("v.m4s"), File("a.m4s"), Path.Combine(_dir, "merged.mp4")), null,
                CancellationToken.None));

        Assert.IsTrue(e.IsDiskFull);
        Assert.IsInstanceOfType<MediaMergeException>(e.InnerException);
        Assert.IsInstanceOfType<IUserActionableException>(e);
    }

    [TestMethod]
    [DataRow("[mp4 @ 0x0] Error writing trailer of merged.mp4.partial: No space left on device", true)]
    [DataRow("Error writing trailer of merged.mp4.partial: Input/output error", false)]
    public async Task FailedTrailerWithExitCodeZero_IsAFailure_AndNothingIsMovedIntoPlace(string line, bool diskFull)
    {
        var output = Path.Combine(_dir, "merged.mp4");
        var runner = new StubRunner {StandardError = [line]};

        Exception? e = null;
        try
        {
            await Create(runner).MuxAsync(new MediaMuxRequest(File("v.m4s"), File("a.m4s"), output), null,
                CancellationToken.None);
        }
        catch (Exception caught)
        {
            e = caught;
        }

        if (diskFull)
        {
            Assert.IsTrue(e is DiskWriteException {IsDiskFull: true}, e?.ToString());
        }
        else
        {
            Assert.IsInstanceOfType<MediaMergeException>(e);
        }

        Assert.IsFalse(System.IO.File.Exists(output));
        Assert.IsFalse(System.IO.File.Exists(output + ".partial"));
    }

    [TestMethod]
    public async Task HarmlessErrorLinesWithExitCodeZero_AreASuccess()
    {
        var output = Path.Combine(_dir, "merged.mp4");
        var runner = new StubRunner {StandardError = ["[flv @ 0x0] Packet mismatch 1 11 0"]};

        await Create(runner).RemuxAsync(File("seg.flv"), output, null, CancellationToken.None);

        Assert.IsTrue(System.IO.File.Exists(output));
    }

    [TestMethod]
    public async Task NotEnoughFreeSpace_FailsBeforeRunning()
    {
        var video = File("v.m4s", 10_000);
        var audio = File("a.m4s", 1_000);
        var runner = new StubRunner();

        // 11 000 bytes of input need 12 100 bytes free.
        var e = await Assert.ThrowsExceptionAsync<DiskWriteException>(() => Create(runner, freeSpace: 12_000)
            .MuxAsync(new MediaMuxRequest(video, audio, Path.Combine(_dir, "merged.mp4")), null,
                CancellationToken.None));

        Assert.IsTrue(e.IsDiskFull);
        Assert.AreEqual(0, runner.Calls.Count);

        await Create(runner, freeSpace: 12_100)
            .MuxAsync(new MediaMuxRequest(video, audio, Path.Combine(_dir, "merged.mp4")), null, CancellationToken.None);
        Assert.AreEqual(1, runner.Calls.Count);
    }

    [TestMethod]
    public async Task UnknownFreeSpace_DoesNotBlock()
    {
        var runner = new StubRunner();

        await Create(runner, freeSpace: null).RemuxAsync(File("seg.flv"), Path.Combine(_dir, "merged.mp4"), null,
            CancellationToken.None);

        Assert.AreEqual(1, runner.Calls.Count);
    }

    [TestMethod]
    public async Task Progress_ReportsPositionAndHeartbeats_EndsAtOne()
    {
        var reports = new ConcurrentQueue<double?>();
        // Driven by what the heartbeat has reported, not by wall-clock windows: a loaded runner only makes it slower.
        var silentHeartbeats = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var halfway = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new StubRunner
        {
            RunAsync = async (stdout, ct) =>
            {
                // Silent at first (heartbeats carry null), then halfway.
                await silentHeartbeats.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                stdout("frame=10");
                stdout("out_time_us=5000000");
                stdout("progress=continue");
                await halfway.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            },
        };

        await Create(runner).RemuxAsync(File("seg.flv"), Path.Combine(_dir, "merged.mp4"),
            new MediaMergeProgress(f =>
            {
                reports.Enqueue(f);
                if (reports.Count(r => r == null) >= 2)
                {
                    silentHeartbeats.TrySetResult();
                }

                if (f == 0.5)
                {
                    halfway.TrySetResult();
                }

                return Task.CompletedTask;
            }, TimeSpan.FromSeconds(10)), CancellationToken.None);

        var list = reports.ToList();
        Assert.IsTrue(list.Count >= 4, $"heartbeats while running, got {list.Count}");
        Assert.IsNull(list[0]);
        Assert.IsTrue(list.Contains(0.5));
        Assert.AreEqual(1d, list[^1]);
    }

    [TestMethod]
    public async Task Progress_WithoutDuration_IsNullWhileRunning()
    {
        var reports = new ConcurrentQueue<double?>();
        var runner = new StubRunner
        {
            RunAsync = async (stdout, ct) =>
            {
                stdout("out_time_ms=5000000");
                await Task.Delay(100, ct);
            },
        };

        await Create(runner).RemuxAsync(File("seg.flv"), Path.Combine(_dir, "merged.mp4"),
            new MediaMergeProgress(f =>
            {
                reports.Enqueue(f);
                return Task.CompletedTask;
            }), CancellationToken.None);

        var list = reports.ToList();
        Assert.IsTrue(list.Take(list.Count - 1).All(r => r == null));
        Assert.AreEqual(1d, list[^1]);
    }

    [TestMethod]
    public async Task FailingProgressCallback_DoesNotFailTheMerge()
    {
        var runner = new StubRunner {RunAsync = (_, ct) => Task.Delay(60, ct)};
        var output = Path.Combine(_dir, "merged.mp4");

        await Create(runner).RemuxAsync(File("seg.flv"), output,
            new MediaMergeProgress(_ => throw new InvalidOperationException("sink broke")), CancellationToken.None);

        Assert.IsTrue(System.IO.File.Exists(output));
    }

    [TestMethod]
    public async Task Cancellation_Propagates_PartialRemoved()
    {
        using var cts = new CancellationTokenSource();
        var output = Path.Combine(_dir, "merged.mp4");
        var runner = new StubRunner
        {
            RunAsync = async (_, ct) =>
            {
                await System.IO.File.WriteAllBytesAsync(output + ".partial", new byte[10], CancellationToken.None);
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
            Create(runner).RemuxAsync(File("seg.flv"), output, new MediaMergeProgress(_ => Task.CompletedTask),
                cts.Token));

        Assert.IsFalse(System.IO.File.Exists(output + ".partial"));
        Assert.IsFalse(System.IO.File.Exists(output));
    }

    [TestMethod]
    [DataRow("out_time_us=1500000", 1.5)]
    [DataRow("out_time_ms=2500000", 2.5)]
    [DataRow("out_time_us=N/A", null)]
    [DataRow("out_time=00:00:01.000000", null)]
    [DataRow("progress=end", null)]
    public void ProgressParser(string line, double? seconds)
    {
        var parser = new FfMpegProgressParser();
        parser.OnLine(line);
        Assert.AreEqual(seconds, parser.Position?.TotalSeconds);
    }

    [TestMethod]
    public void ProgressParser_FractionIsClamped()
    {
        var parser = new FfMpegProgressParser();
        Assert.IsNull(parser.Fraction(TimeSpan.FromSeconds(10)));
        parser.OnLine("out_time_us=12000000");
        Assert.AreEqual(1d, parser.Fraction(TimeSpan.FromSeconds(10)));
        Assert.IsNull(parser.Fraction(null));
        Assert.IsNull(parser.Fraction(TimeSpan.Zero));
    }

    [TestMethod]
    public void DiskSpace_OfTheTempFolder_IsKnown()
    {
        Assert.IsTrue(DiskSpace.TryGetAvailableFreeSpace(_dir) > 0);
    }

    /// <summary>
    /// Real FFmpeg, only where it is installed (not in CI): mux and concat of generated test sources.
    /// </summary>
    [TestMethod]
    [TestCategory("RequiresFfmpeg")]
    public async Task RealFfmpeg_MuxAndConcat()
    {
        var ffmpeg = FindOnPath("ffmpeg");
        if (ffmpeg == null)
        {
            Assert.Inconclusive("ffmpeg is not on PATH.");
            return;
        }

        async Task Generate(string args)
        {
            using var process = Process.Start(new ProcessStartInfo(ffmpeg, args) {RedirectStandardError = true})!;
            await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.AreEqual(0, process.ExitCode, args);
        }

        var video = Path.Combine(_dir, "v.mp4");
        var audio = Path.Combine(_dir, "a.m4a");
        await Generate($"-hide_banner -y -f lavfi -i testsrc=duration=2:size=160x120:rate=10 -c:v mpeg4 \"{video}\"");
        await Generate($"-hide_banner -y -f lavfi -i sine=duration=2 -c:a aac \"{audio}\"");
        var merger = new FfMpegMediaMerger(_ => Task.FromResult(ffmpeg), new CliWrapFfMpegProcessRunner(),
            DiskSpace.TryGetAvailableFreeSpace, TimeProvider.System, NullLogger<FfMpegMediaMerger>.Instance);

        var muxed = Path.Combine(_dir, "muxed.mp4");
        await merger.MuxAsync(new MediaMuxRequest(video, audio, muxed), null, CancellationToken.None);
        Assert.IsTrue(new FileInfo(muxed).Length > 0);

        var concatenated = Path.Combine(_dir, "it's concat.mp4");
        await merger.ConcatAsync([video, video], concatenated, null, CancellationToken.None);
        Assert.IsTrue(new FileInfo(concatenated).Length > new FileInfo(video).Length);
    }

    private static string? FindOnPath(string name)
    {
        var names = OperatingSystem.IsWindows() ? new[] {name + ".exe"} : new[] {name};
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(dir => names.Select(n => Path.Combine(dir, n)))
            .FirstOrDefault(System.IO.File.Exists);
    }

    internal sealed record RunnerCall(string Executable, IReadOnlyList<string> Arguments);

    /// <summary>Stands in for FFmpeg: records the call, optionally streams progress, writes the output given as the
    /// last argument when it "succeeds".</summary>
    internal sealed class StubRunner : IFfMpegProcessRunner
    {
        public List<RunnerCall> Calls { get; } = [];
        public int ExitCode { get; init; }
        public int OutputBytes { get; init; } = 16;
        public string[] StandardError { get; init; } = [];
        public Action<IReadOnlyList<string>>? OnRun { get; init; }
        public Func<Action<string>, CancellationToken, Task>? RunAsync { get; init; }

        async Task<int> IFfMpegProcessRunner.RunAsync(string executable, IReadOnlyList<string> arguments,
            Action<string> onStandardOutputLine, Action<string> onStandardErrorLine, CancellationToken ct)
        {
            Calls.Add(new RunnerCall(executable, arguments));
            OnRun?.Invoke(arguments);
            if (RunAsync != null)
            {
                await RunAsync(onStandardOutputLine, ct);
            }

            foreach (var line in StandardError)
            {
                onStandardErrorLine(line);
            }

            if (ExitCode == 0)
            {
                await System.IO.File.WriteAllBytesAsync(arguments[^1], new byte[OutputBytes], ct);
            }
            else
            {
                // FFmpeg leaves a truncated output behind on failure.
                await System.IO.File.WriteAllBytesAsync(arguments[^1], new byte[3], ct);
            }

            return ExitCode;
        }
    }
}
