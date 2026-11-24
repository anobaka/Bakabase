using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.CloudDownloads.PikPakCli;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class PikPakPayloadActivityTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pikpak-payload-" + Guid.NewGuid().ToString("N"));

    [TestInitialize]
    public void SetUp() => Directory.CreateDirectory(_root);

    [TestCleanup]
    public void CleanUp() => Directory.Delete(_root, true);

    [TestMethod]
    public void OnlyNewWritesToTheSamePartialFileSignalActivity()
    {
        var path = Path.Combine(_root, "sample.payload.part");
        var activity = new PikPakPayloadActivity(path);
        Assert.IsFalse(activity.HasWritten());
        using var write = File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        write.SetLength(16 * 1024 * 1024);
        var preallocated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, preallocated);

        Assert.IsFalse(activity.HasWritten());
        Assert.IsFalse(activity.HasWritten());

        write.WriteByte(42);
        write.Flush();
        File.SetLastWriteTimeUtc(path, preallocated.AddSeconds(1));
        Assert.AreEqual(16 * 1024L * 1024, write.Length);
        Assert.IsTrue(activity.HasWritten());
        Assert.IsFalse(activity.HasWritten());

        File.Move(path, Path.Combine(_root, "finished.payload"));
        File.WriteAllBytes(path, [1, 2]);
        Assert.IsFalse(activity.HasWritten());
        Assert.IsFalse(activity.HasWritten());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeRangedWritesRefreshActivityWithoutChangingFileLength(bool reportProcessWrites)
    {
        var output = Path.Combine(_root, "sample.payload");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var cli = new RangedDownload(reportProcessWrites);
        var account = new PikPakCliAccountLease("test", "generation", _root, cli);
        var callbacks = 0;

        await PikPakPayloadActivity.DownloadAsync(account, "file", output, () =>
        {
            Assert.AreEqual(RangedDownload.Length, new FileInfo(output + ".part").Length);
            callbacks++;
            cli.Observed.TrySetResult();
            return Task.CompletedTask;
        }, deadline.Token);

        Assert.AreEqual(1, callbacks);
        Assert.AreEqual(RangedDownload.Length, new FileInfo(output).Length);
        Assert.IsFalse(File.Exists(output + ".part"));
    }

    [TestMethod]
    public async Task LinuxPayloadCountsFixedLengthWritesWithFrozenTimestampsAndStopsWhenIdle()
    {
        if (!OperatingSystem.IsLinux() || !Environment.Is64BitProcess) return;
        var partial = Path.Combine(_root, "frozen.payload.part");
        var startWriting = Path.Combine(_root, "write");
        var fixedTimestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activity = 0;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        // The same child that owns the download accounts for its completed range writer.
        // Steady diagnostic traffic is deliberately present during both idle windows.
        var command = RunLinuxPayload("""
            truncate -s 16777216 "$1"
            touch -m -t 202601010000 "$1"
            while [ ! -f "$2" ]; do printf 'waiting\n' >&2; sleep .1; done
            dd if=/dev/zero of="$1" bs=4096 count=8 seek=17 conv=notrunc,fsync status=none
            touch -m -t 202601010000 "$1"
            while :; do printf 'waiting\n' >&2; sleep .1; done
            """, [partial, startWriting], cancellation.Token, () =>
        {
            Interlocked.Increment(ref activity);
            firstWrite.TrySetResult();
        });
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(2300), cancellation.Token);
            Assert.AreEqual(RangedDownload.Length, new FileInfo(partial).Length);
            Assert.AreEqual(fixedTimestamp, File.GetLastWriteTimeUtc(partial));
            Assert.AreEqual(0, Volatile.Read(ref activity));

            File.WriteAllBytes(startWriting, []);
            await firstWrite.Task.WaitAsync(cancellation.Token);
            Assert.AreEqual(RangedDownload.Length, new FileInfo(partial).Length);
            Assert.AreEqual(fixedTimestamp, File.GetLastWriteTimeUtc(partial));

            var reported = Volatile.Read(ref activity);
            await Task.Delay(TimeSpan.FromMilliseconds(2300), cancellation.Token);
            Assert.AreEqual(reported, Volatile.Read(ref activity));
        }
        finally
        {
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => { await command; });
        }
    }

    [TestMethod]
    public async Task LinuxPayloadThatExitsImmediatelyDoesNotReportWriteActivity()
    {
        if (!OperatingSystem.IsLinux() || !Environment.Is64BitProcess) return;
        var result = await RunLinuxPayload("""printf '{"success":true}'""", [], default,
            () => Assert.Fail("A command that wrote only to stdout reported a storage write."));
        Assert.IsTrue(result.GetProperty("success").GetBoolean());
    }

    [TestMethod]
    public async Task HashObserverOnlySignalsAfterNonemptyReads()
    {
        var content = new byte[CloudPayloadHash.BufferSize + 1];
        RandomNumberGenerator.Fill(content);
        using var input = new MemoryStream(content);
        using var empty = new MemoryStream();
        var reads = 0;
        ValueTask Observe()
        {
            reads++;
            return ValueTask.CompletedTask;
        }

        await CloudPayloadHash.ComputeAsync(empty, default, Observe);
        Assert.AreEqual(0, reads);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(content)),
            await CloudPayloadHash.ComputeAsync(input, default, Observe));
        Assert.AreEqual(2, reads);
    }

    private static Task<JsonElement> RunLinuxPayload(string script, IReadOnlyList<string> paths, CancellationToken ct,
        Action onWrite)
    {
        var start = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["TZ"] = "UTC";
        foreach (var argument in new[] {"-eu", "-c", script, "fixture"}.Concat(paths))
            start.ArgumentList.Add(argument);
        return PikPakCliProcess.RunProcessAsync(start, ["download"], ct, payload: true, observeProgress: onWrite);
    }

    private sealed class RangedDownload(bool reportProcessWrites) : IPikPakCliProcess
    {
        internal const long Length = 16 * 1024 * 1024;
        internal readonly TaskCompletionSource Observed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<JsonElement> RunAsync(string profile, IReadOnlyList<string> arguments,
            CancellationToken ct, bool payload = false, Action? observeProgress = null)
        {
            Assert.IsTrue(payload);
            CollectionAssert.AreEqual(new[] {"download", "file", "--output"}, arguments.Take(3).ToArray());
            var output = arguments[3];
            var partial = output + ".part";
            await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write,
                             FileShare.ReadWrite | FileShare.Delete))
            {
                file.SetLength(Length);
                var frozenTimestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                while (!Observed.Task.IsCompleted)
                {
                    file.WriteByte(42);
                    await file.FlushAsync(ct);
                    if (reportProcessWrites)
                    {
                        File.SetLastWriteTimeUtc(partial, frozenTimestamp);
                        Assert.IsNotNull(observeProgress);
                        observeProgress();
                    }
                    // Each write replaces bytes within the allocation, as in the CLI's ranged transfer.
                    file.Position = (file.Position + 1024 * 1024) % Length;
                    await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
                }
            }
            File.Move(partial, output);
            return JsonSerializer.SerializeToElement(new {success = true});
        }
    }
}
