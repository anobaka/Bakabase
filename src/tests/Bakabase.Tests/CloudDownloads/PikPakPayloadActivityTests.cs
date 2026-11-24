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

    [TestMethod]
    public async Task NativeRangedWritesRefreshActivityWithoutChangingFileLength()
    {
        var output = Path.Combine(_root, "sample.payload");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var cli = new RangedDownload();
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

    private sealed class RangedDownload : IPikPakCliProcess
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
                while (!Observed.Task.IsCompleted)
                {
                    file.WriteByte(42);
                    await file.FlushAsync(ct);
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
