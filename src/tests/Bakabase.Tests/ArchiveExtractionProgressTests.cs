using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using Bakabase.InsideWorld.Business.Components.Compression;
using CliWrap;

namespace Bakabase.Tests;

[TestClass]
public sealed class ArchiveExtractionProgressTests
{
    [TestMethod]
    public async Task BackspaceAndCarriageReturnProgressArrivesBeforeNewlineOrEof()
    {
        var pipe = new Pipe();
        var records = new ConcurrentQueue<string>();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reading = SevenZipProgressOutput.CreatePipe(line =>
        {
            records.Enqueue(line);
            if (line == "72%") reported.TrySetResult();
        }).CopyFromAsync(pipe.Reader.AsStream(), stop.Token);
        foreach (var fragment in new[] {"  0%", "\b\b\b\b    \b\b\b\b 2", "5", "% 1 - data.zip", "\r    \r 7", "2", "% "})
            await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(fragment), stop.Token);
        await reported.Task.WaitAsync(stop.Token);
        Assert.IsFalse(reading.IsCompleted, "Progress must not wait for EOF.");
        CollectionAssert.AreEqual(new[] {"0%", "25%", "72%"}, records.ToArray());
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("1 - file.zip\r100%\n"), stop.Token);
        await pipe.Writer.CompleteAsync(); await reading;
        CollectionAssert.AreEqual(new[] {"0%", "25%", "72%", "100%"}, records.ToArray());
    }

    [TestMethod]
    public async Task FragmentedUtf8DiagnosticsArePreservedAndFilenamePercentagesAreNotProgress()
    {
        const string text = "ERROR: 密码错误 90%.zip\nExtracting 67% file.zip\r70%.zip\n  8% 1 - picture99%.jpg\b\b\n101%\n";
        var records = new List<string>();
        var raw = new StringBuilder();
        using var stream = new OneByteStream(Encoding.UTF8.GetBytes(text));
        await PipeTarget.Merge(PipeTarget.ToStringBuilder(raw, Encoding.UTF8),
            SevenZipProgressOutput.CreatePipe(records.Add)).CopyFromAsync(stream);
        Assert.AreEqual(text, raw.ToString(), "The tool's original diagnostic output must remain intact.");
        CollectionAssert.AreEqual(new[] {8}, records.Where(r => SevenZipProgressOutput.TryPercentage(r, out _))
            .Select(r => int.Parse(r.TrimEnd('%'))).ToArray());
        CollectionAssert.Contains(records, "ERROR: 密码错误 90%.zip");
        CollectionAssert.Contains(records, "70%.zip");
    }

    [TestMethod]
    public async Task CancelledPipeDoesNotFlushBufferedProgressOrNotifyAfterReturning()
    {
        var pipe = new Pipe(); var records = new List<string>();
        using var stop = new CancellationTokenSource();
        var reading = SevenZipProgressOutput.CreatePipe(records.Add).CopyFromAsync(pipe.Reader.AsStream(), stop.Token);
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(" 23%"));
        stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => reading);
        Assert.AreEqual(0, records.Count);
        await pipe.Writer.CompleteAsync();
    }

    [TestMethod]
    public async Task AsyncUiUpdateIsJoinedBeforeExtractionReturns()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = false;
        var running = ArchiveExtractionProgress.RunAsync((emit, _) =>
        {
            emit("57%"); return Task.FromResult(42);
        }, async value =>
        {
            Assert.AreEqual(57, value); entered.SetResult();
            await release.Task;
            Assert.IsFalse(returned);
        }, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(running.IsCompleted);
        release.SetResult(); Assert.AreEqual(42, await running); returned = true;
    }

    [TestMethod]
    public async Task AsyncUpdateFailureStopsTheExtractionAndIsObserved()
    {
        var stopped = false;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ArchiveExtractionProgress.RunAsync(async (emit, ct) =>
        {
            emit("2%");
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { stopped = true; }
            return 0;
        }, _ => Task.FromException(new InvalidOperationException("fixture notification failed")), default));
        Assert.AreEqual("fixture notification failed", error.Message); Assert.IsTrue(stopped);
    }

    [TestMethod]
    public async Task CancelledExtractionJoinsItsProgressReaderAndPreservesCancellation()
    {
        using var stop = new CancellationTokenSource();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<int>();
        var running = ArchiveExtractionProgress.RunAsync(async (emit, ct) =>
        {
            emit("ERROR: 98%.zip"); emit("12%");
            await Task.Delay(Timeout.Infinite, ct); return 0;
        }, value => { seen.Add(value); delivered.TrySetResult(); return Task.CompletedTask; }, stop.Token);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => running);
        CollectionAssert.AreEqual(new[] {12}, seen);
    }

    [TestMethod]
    public async Task FileExplorerReceivesLiveProgressAndAwaitsTheLastUpdate()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The fake tool is a Unix shell script.");
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "archive-progress-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var tool = Path.Combine(root, "tool");
            await File.WriteAllTextAsync(tool, """
                #!/bin/sh
                case "$1" in
                l) printf '%s\n' '--' 'Type = zip' '----------' 'Path = payload.txt' 'Folder = -' 'Size = 2' '';;
                x) printf '  0%%\b\b\b\b    \b\b\b\b 42%% ' >&2
                   while [ ! -e "$0.continue" ]; do sleep 0.05; done
                   for arg do case "$arg" in -o*) dest=${arg#-o};; esac; done
                   mkdir -p "$dest"; printf 'ok' > "$dest/payload.txt"
                   printf '\r100%%\n' >&2;;
                esac
                """);
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var source = Path.Combine(root, "source.zip"); await File.WriteAllTextAsync(source, "synthetic source");
            var output = Path.Combine(root, "output"); var seen = new List<int>();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await new TestableCompressedFileService(tool).ExtractForFileExplorerAsync(source, output, null,
                async percentage =>
                {
                    seen.Add(percentage);
                    if (percentage == 42)
                    {
                        Assert.IsFalse(File.Exists(Path.Combine(output, "payload.txt")));
                        await File.WriteAllTextAsync(tool + ".continue", "continue", stop.Token);
                    }
                    if (percentage == 100) await Task.Yield();
                }, stop.Token);
            Assert.AreEqual(0, result.ExitCode); CollectionAssert.Contains(seen, 42);
            Assert.AreEqual(100, seen.Last());
            Assert.AreEqual("ok", await File.ReadAllTextAsync(Path.Combine(output, "payload.txt")));
            StringAssert.Contains(result.StandardError, "\b");
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class OneByteStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
    }
}
