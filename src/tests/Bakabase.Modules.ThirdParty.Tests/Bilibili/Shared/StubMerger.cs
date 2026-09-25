using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

// Shared with Bakabase.Tests, which links this file (see FakeBilibiliHttp.cs).
namespace Bakabase.Modules.ThirdParty.Tests.Bilibili.Shared;

internal sealed record MergeCall(
    string Operation,
    IReadOnlyList<string> Inputs,
    string Output,
    bool TagHevcAsHvc1 = false,
    bool AllowExperimentalCodecs = false)
{
    public string? VideoInput => Operation == StubMerger.Mux ? Inputs[0] : null;
    public string? AudioInput => Operation == StubMerger.Mux && Inputs.Count > 1 ? Inputs[1] : null;
}

/// <summary>
/// Records merges and writes the concatenated inputs as the output (CI has no ffmpeg).
/// <see cref="Fail"/> decides per call whether to throw a <see cref="MediaMergeException"/> instead.
/// </summary>
internal sealed class StubMerger : IMediaMerger
{
    public const string Mux = "mux";
    public const string Remux = "remux";
    public const string Concat = "concat";
    public const int FailureExitCode = 234;

    public ConcurrentQueue<MergeCall> Calls { get; } = new();

    /// <summary>Returns true to make the call fail.</summary>
    public Func<MergeCall, bool>? Fail { get; set; }

    public Task MuxAsync(MediaMuxRequest request, MediaMergeProgress? progress, CancellationToken ct) =>
        RunAsync(new MergeCall(Mux,
            request.AudioPath == null ? [request.VideoPath] : [request.VideoPath, request.AudioPath],
            request.OutputPath, request.TagHevcAsHvc1, request.AllowExperimentalCodecs), progress, ct);

    public Task RemuxAsync(string inputPath, string outputPath, MediaMergeProgress? progress, CancellationToken ct) =>
        RunAsync(new MergeCall(Remux, [inputPath], outputPath), progress, ct);

    public Task ConcatAsync(IReadOnlyList<string> inputPaths, string outputPath, MediaMergeProgress? progress,
        CancellationToken ct) =>
        RunAsync(new MergeCall(Concat, inputPaths.ToList(), outputPath), progress, ct);

    private async Task RunAsync(MergeCall call, MediaMergeProgress? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls.Enqueue(call);
        foreach (var input in call.Inputs)
        {
            Assert.IsTrue(File.Exists(input), $"merge input missing: {Path.GetFileName(input)}");
        }

        if (progress != null)
        {
            await progress.Report(null);
            await progress.Report(0.5);
        }

        if (Fail?.Invoke(call) == true)
        {
            throw new MediaMergeException($"ffmpeg ({call.Operation}) exited with code {FailureExitCode}",
                FailureExitCode, "stub failure");
        }

        await using var output = File.Create(call.Output);
        foreach (var input in call.Inputs)
        {
            await using var source = File.OpenRead(input);
            await source.CopyToAsync(output, ct);
        }

        if (progress != null)
        {
            await progress.Report(1);
        }
    }
}
