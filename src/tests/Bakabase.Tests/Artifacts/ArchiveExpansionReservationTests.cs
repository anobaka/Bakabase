using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class ArchiveExpansionReservationTests
{
    private const long MiB = 1024 * 1024;
    private const long Floor = 100 * MiB;
    private const long Headroom = StorageWriteGuard.ReservationHeadroomBytes;
    private static readonly StorageWriteContext Owner = new("workflow", "17");
    private static readonly FileProcessingPlan Plan = new()
    {
        Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive"}]
    };
    private string root = null!;
    private string input = null!;
    private long free;

    [TestInitialize]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "archive-budget-" + Guid.NewGuid().ToString("N"));
        input = Path.Combine(root, "source");
        Directory.CreateDirectory(input);
        Interlocked.Exchange(ref free, Floor + Headroom + 5 * MiB);
    }

    [TestCleanup]
    public void CleanUp() => Directory.Delete(root, true);

    private string Input(string name)
    {
        var path = Path.Combine(input, name);
        File.WriteAllText(path, name);
        return path;
    }

    private StorageWriteGuard Shared() => new(new Policy(), _ => ("one-volume", Interlocked.Read(ref free)),
        TimeSpan.FromMilliseconds(5));

    private Task<FileProcessingPlanResult> Execute(Archives archives, IStorageWriteGuard guard,
        CancellationToken token, params string[] files) =>
        new FileProcessingPlanExecutor(archives, writeGuard: guard).ExecuteAsync(Plan, input, files,
            Path.Combine(root, "checkpoint"), password: "fixture-pass", ct: token);

    [TestMethod, Timeout(10000)]
    public async Task A_cloud_lease_and_an_extraction_group_share_one_volume_budget()
    {
        var shared = Shared();
        var admission = new ReservationTrace(shared);
        var archives = new Archives { Estimates = { ["first.zip"] = MiB } };
        var zip = Input("first.zip");
        using var owner = StorageWriteScope.Enter(Owner);
        using var lifetime = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloud = shared.RunReservedAsync([new(Path.Combine(root, "cloud"), MiB)], new("downloadTask", "19"),
            async token => { started.SetResult(); await release.Task.WaitAsync(token); return true; }, ct: lifetime.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var archive = Execute(archives, admission, lifetime.Token, zip);
            try
            {
                await admission.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.AreEqual(0, archives.Extracts.Count);
                Assert.IsFalse(archive.IsCompleted);
                release.TrySetResult();
                var result = await archive.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.IsTrue(result.Completed, result.Message);
                CollectionAssert.AreEqual(new[] {"first.zip"}, archives.Extracts.ToArray());
                Assert.AreEqual(1, admission.Budgets.Count);
                Assert.AreEqual(MiB, admission.Budgets.Single().MaximumAdditionalBytes);
            }
            finally
            {
                lifetime.Cancel();
                try { await archive; } catch (OperationCanceledException) { }
            }
        }
        finally
        {
            release.TrySetResult();
            try { await cloud; } catch (OperationCanceledException) { }
        }
    }

    [TestMethod]
    public async Task An_impossible_group_waits_before_extraction_and_resumes_the_same_checkpoint()
    {
        var guard = new ReservationTrace(Shared());
        var archives = new Archives { Estimates = { ["a.zip"] = 1, ["b.zip"] = 256 * MiB } };
        var good = Input("a.zip");
        var tooLarge = Input("b.zip");
        using var owner = StorageWriteScope.Enter(Owner);

        var wait = await Execute(archives, guard, default, good, tooLarge);
        Assert.IsFalse(wait.Completed);
        Assert.AreEqual("StorageUnavailable", wait.FailureCode);
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, wait.Message);
        CollectionAssert.AreEqual(new[] {"a.zip"}, archives.Extracts.ToArray());
        CollectionAssert.AreEqual(new[] {"a.zip", "b.zip"}, archives.Listed.ToArray());
        var probes = archives.Probes.Count;
        var stillFull = await Execute(archives, guard, default, good, tooLarge);
        Assert.AreEqual("StorageUnavailable", stillFull.FailureCode);
        Assert.AreEqual(probes, archives.Probes.Count, "An unchanged impossible group already has a bounded receipt.");
        Assert.AreEqual(2, archives.Listed.Count);
        Assert.AreEqual(1, archives.Extracts.Count);

        Interlocked.Exchange(ref free, Floor + Headroom + 300 * MiB);
        var result = await Execute(archives, guard, default, good, tooLarge);
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEqual(new[] {"a.zip", "b.zip"}, archives.Extracts.ToArray());
        Assert.AreEqual(1, archives.Listed.Count(n => n == "a.zip"));
        Assert.AreEqual("a.zip", await File.ReadAllTextAsync(good));
        Assert.AreEqual("b.zip", await File.ReadAllTextAsync(tooLarge));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Unknown_sizes_remain_monitored_and_known_zero_is_a_real_reservation(bool knownZero)
    {
        var trace = new ReservationTrace(Shared());
        var archives = new Archives { Estimates = { ["sample.zip"] = knownZero ? 0L : null } };
        using var owner = StorageWriteScope.Enter(Owner);
        var result = await Execute(archives, trace, default, Input("sample.zip"));
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual(1, archives.Listed.Count);
        Assert.AreEqual(1, archives.Extracts.Count);
        Assert.AreEqual(knownZero ? 1 : 0, trace.Budgets.Count);
        if (knownZero) Assert.AreEqual(0L, trace.Budgets.Single().MaximumAdditionalBytes);
        else Assert.IsTrue(trace.MonitoredPaths.Contains(archives.LastExtraction!.Directory));
    }

    [TestMethod]
    public async Task An_ordinary_guard_admits_known_sizes_before_calling_the_extractor()
    {
        var trace = new MonitorTrace(Shared());
        var archives = new Archives { Estimates = { ["large.zip"] = 256 * MiB } };
        using var owner = StorageWriteScope.Enter(Owner);
        var result = await Execute(archives, trace, default, Input("large.zip"));
        Assert.AreEqual("StorageUnavailable", result.FailureCode);
        Assert.IsTrue(trace.Allocations.Contains(256 * MiB));
        Assert.AreEqual(0, archives.Extracts.Count);
    }

    [TestMethod]
    public async Task A_failed_password_probe_does_not_authorize_listing_or_writing()
    {
        var guard = new ReservationTrace(Shared());
        var archives = new Archives { PasswordAccepted = false };
        using var owner = StorageWriteScope.Enter(Owner);
        var result = await Execute(archives, guard, default, Input("locked.zip"));
        Assert.IsTrue(result.NeedsPassword);
        Assert.AreEqual(1, archives.Probes.Count);
        Assert.AreEqual(0, archives.Listed.Count);
        Assert.AreEqual(0, archives.Extracts.Count);
        Assert.AreEqual(0, guard.Budgets.Count);
    }

    private sealed class Policy : IStorageWritePolicyProvider
    {
        public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default) =>
            Task.FromResult(Floor);
    }

    private class MonitorTrace(StorageWriteGuard shared) : IStorageWriteGuard
    {
        protected StorageWriteGuard Inner { get; } = shared;
        public List<long> Allocations { get; } = [];
        public List<string> MonitoredPaths { get; } = [];
        public Task CheckAsync(string path, StorageWriteContext? context, CancellationToken ct = default) =>
            Inner.CheckAsync(path, context, ct);
        public Task CheckAllocationAsync(string path, long bytes, StorageWriteContext? context, CancellationToken ct = default)
        {
            Allocations.Add(bytes);
            return Inner.CheckAllocationAsync(path, bytes, context, ct);
        }
        public Task<T> RunAsync<T>(IReadOnlyList<string> paths, StorageWriteContext? context,
            Func<CancellationToken,Task<T>> writer, CancellationToken ct = default)
        {
            MonitoredPaths.AddRange(paths);
            return Inner.RunAsync(paths, context, writer, ct);
        }
        public Task CopyAsync(Stream source, Stream destination, string path, StorageWriteContext? context,
            CancellationToken ct = default) => Inner.CopyAsync(source, destination, path, context, ct);
    }

    private sealed class ReservationTrace(StorageWriteGuard shared) : MonitorTrace(shared), IStorageWriteReservationGuard
    {
        public List<StorageWriteBudget> Budgets { get; } = [];
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T> RunReservedAsync<T>(IReadOnlyList<StorageWriteBudget> budgets, StorageWriteContext? context,
            Func<CancellationToken,Task<T>> writer, Func<Task>? waiting = null, CancellationToken ct = default)
        {
            Budgets.AddRange(budgets);
            return Inner.RunReservedAsync(budgets, context, writer, async () =>
            {
                Waiting.TrySetResult();
                if (waiting != null) await waiting();
            }, ct);
        }
    }

    private sealed class Archives : IArchiveExtractionService
    {
        public Dictionary<string,long?> Estimates { get; } = [];
        private readonly Dictionary<string, ArchiveExtractionRequest> listedRequests = [];
        public List<string> Listed { get; } = [];
        public List<string> Extracts { get; } = [];
        public List<string> Probes { get; } = [];
        public bool PasswordAccepted { get; init; } = true;
        public ArchiveExtractionRequest? LastExtraction { get; private set; }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct)
        {
            Probes.Add(Path.GetFileName(entry));
            return Task.FromResult(new ArchivePasswordProbe(PasswordAccepted, PasswordAccepted ? "fixture-pass" : null,
                [], "", "") {PasswordRejected = !PasswordAccepted});
        }
        public Task<long?> TryGetExpandedBytesAsync(ArchiveExtractionRequest request, CancellationToken ct)
        {
            var name = Path.GetFileName(request.Files[0]);
            Assert.IsTrue(PasswordAccepted && Probes.Contains(name), "List the same accepted group.");
            Assert.AreEqual("fixture-pass", request.Password);
            Listed.Add(name);
            listedRequests[name] = request;
            return Task.FromResult(Estimates[name]);
        }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress,
            CancellationToken ct)
        {
            LastExtraction = request;
            var name = Path.GetFileName(request.Files[0]);
            if (listedRequests.TryGetValue(name, out var listed))
            {
                Assert.AreEqual(listed.Directory, request.Directory);
                Assert.AreEqual(listed.Password, request.Password);
                Assert.AreEqual(listed.OpenOptions, request.OpenOptions);
                CollectionAssert.AreEqual(listed.Files.ToArray(), request.Files.ToArray());
            }
            Extracts.Add(name);
            Directory.CreateDirectory(request.Directory);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, name + ".txt"), name, ct);
            return new(true, request.Directory, "");
        }
    }
}
