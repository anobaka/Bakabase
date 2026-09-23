using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Queries;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Queries;

/// <summary>Records actual timings/allocations; assertions concern correctness, not invented performance thresholds.</summary>
[TestClass]
[DoNotParallelize]
public sealed class QueryPerformanceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(10_000)]
    [DataRow(100_000)]
    [TestCategory("FederationPerformance")]
    public async Task CompleteBoundedTraversalRecordsMeasuredCost(int count)
    {
        var limits = new FederationQueryLimits { MaxSnapshotBytes = 64 * 1024 * 1024 };
        using var peer = new TestPeer("perf-node", limits, null);
        peer.Reader.Rows = Enumerable.Range(1, count).Select(i => new LocalResourceProjection(i,
            $"Title {count - i:D6}", $"{i:D6}.mkv", true, [1])).ToList();
        using var coordinator = new FederatedQueryCoordinator(new FixedTargets(peer), limits);
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var watch = Stopwatch.StartNew();
        var page = await coordinator.CreateAsync("ui", new() { NodeIds = ["perf-node"], PageSize = 200 });
        var firstPageMs = watch.Elapsed.TotalMilliseconds;
        var visited = page.Items.Length;
        var firstId = page.Items[0].Ref.ResourceId;
        var lastId = page.Items[^1].Ref.ResourceId;
        var pageTimes = new List<double>();
        while (page.NextCursor != null)
        {
            var began = watch.Elapsed.TotalMilliseconds;
            page = await coordinator.ReadAsync("ui", page.SessionId, page.NextCursor);
            pageTimes.Add(watch.Elapsed.TotalMilliseconds - began);
            Assert.AreEqual(lastId - 1, page.Items[0].Ref.ResourceId);
            lastId = page.Items[^1].Ref.ResourceId;
            visited += page.Items.Length;
        }
        watch.Stop();
        Assert.AreEqual(count, visited);
        Assert.AreEqual(count, firstId);
        Assert.AreEqual(1, lastId);
        var measured = new
        {
            kind = "frozen-projection-and-complete-coordinator-traversal", resources = count,
            snapshotBudgetBytes = limits.MaxSnapshotBytes, firstPageMs, totalMs = watch.Elapsed.TotalMilliseconds,
            subsequentPageP50Ms = Percentile(pageTimes, .5), subsequentPageP95Ms = Percentile(pageTimes, .95),
            allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocatedBefore,
            processWorkingSetBytes = Environment.WorkingSet, blockReads = peer.Client.ReadCalls,
            os = RuntimeInformation.OSDescription, runtime = RuntimeInformation.FrameworkDescription,
            processorCount = Environment.ProcessorCount
        };
        var json = JsonSerializer.Serialize(measured);
        TestContext.WriteLine(json);
        var output = Environment.GetEnvironmentVariable("BAKABASE_FEDERATION_PERF_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) await File.AppendAllTextAsync(output, json + Environment.NewLine);
        await coordinator.ReleaseAsync("ui", page.SessionId);
        Assert.IsTrue(peer.Client.ReleaseCalls > 0);
    }

    private static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        return values[(int)Math.Floor((values.Count - 1) * p)];
    }
}
