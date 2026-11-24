using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Service.Components.Acquisition.Planning;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionThroughputTests
{
    [TestMethod]
    public void Aggregate_tracks_independent_directions_and_only_acquisition_owned_bytes()
    {
        var owners = new AcquisitionThroughputOwners([
            new(1, "copy", true, [1500d, 500d]),
            new(2, "verify", true, [null]),
            new(3, "queued", false, [])
        ], new Dictionary<(string, string), int> { [("workflow", "99")] = 1, [("downloadTask", "17")] = 2 });
        var tasks = AcquisitionThroughputSampler.Measure(owners, [
            new("workflow", "99", 10000, 5000, true, true),
            new("downloadTask", "17", 2500, 0, true, false),
            new("independentDownload", "12", 50000, 50000, true, true)
        ], 2.5);
        Assert.AreEqual(2, tasks.Count);
        Assert.AreEqual(new AcquisitionThroughputRates(2000, 4000, 2000, false, false), tasks.Single(t => t.TaskId == 1).Rates);
        Assert.AreEqual(new AcquisitionThroughputRates(null, 1000, null, true, true), tasks.Single(t => t.TaskId == 2).Rates);
        Assert.AreEqual(new AcquisitionThroughputRates(2000, 5000, 2000, true, true), AcquisitionThroughputSampler.Total(tasks));
    }

    [TestMethod]
    public void Includes_final_received_bytes_and_provider_rates_in_the_same_window()
    {
        var owners = new AcquisitionThroughputOwners([
            new(1, "completed within one interval", false, [], ReceivedBytes: 1250),
            new(2, "multiple download methods", true, [200d, null], ReceivedBytes: 500),
            new(3, "waiting for response", true, [], ReceivedBytes: 0)
        ], new Dictionary<(string, string), int>());
        var tasks = AcquisitionThroughputSampler.Measure(owners, [], 2.5);
        Assert.AreEqual(500d, tasks.Single(t => t.TaskId == 1).Rates.DownloadBytesPerSecond);
        Assert.AreEqual(400d, tasks.Single(t => t.TaskId == 2).Rates.DownloadBytesPerSecond);
        Assert.AreEqual(0d, tasks.Single(t => t.TaskId == 3).Rates.DownloadBytesPerSecond);
        Assert.AreEqual(900d, AcquisitionThroughputSampler.Total(tasks).DownloadBytesPerSecond);
        Assert.IsTrue(AcquisitionThroughputSampler.Total(tasks).DownloadPartial);
    }

    [TestMethod]
    public void Idle_zero_missing_measurement_and_estimated_io_remain_distinguishable()
    {
        Assert.AreEqual(new AcquisitionThroughputRates(0, 0, 0, false, false), AcquisitionThroughputSampler.Total([]));
        var owners = new AcquisitionThroughputOwners([new(11, null, true, [double.NaN, 0])],
            new Dictionary<(string, string), int> { [("workflow", "42")] = 11 });
        var unknown = AcquisitionThroughputSampler.Measure(owners, [], 5).Single().Rates;
        Assert.AreEqual(new AcquisitionThroughputRates(0, null, null, true, true), unknown);
        var zero = AcquisitionThroughputSampler.Measure(owners, [new("workflow", "42", 0, 0, true, true)], 5).Single().Rates;
        Assert.AreEqual(new AcquisitionThroughputRates(0, 0, 0, true, false), zero);
        var archive = AcquisitionThroughputSampler.Measure(owners, [new("workflow", "42", 0, 500, true, true, true)], 5).Single().Rates;
        Assert.AreEqual(new AcquisitionThroughputRates(0, 0, 100, true, true), archive);
    }

    [TestMethod]
    public void History_is_time_bounded_and_overview_omits_the_per_task_payload()
    {
        var history = new AcquisitionThroughputHistory();
        Assert.IsNull(history.Get(true).Latest);
        var start = DateTimeOffset.Parse("2026-01-01T13:00:00+08:00");
        var idle = AcquisitionThroughputSampler.Total([]);
        for (var i = 0; i <= 60; i++)
            history.Append(start.AddSeconds(5 * i), idle, [new(7, "example", idle)]);
        var detail = history.Get(true);
        Assert.AreEqual(60, detail.History.Count);
        Assert.AreEqual(start.AddSeconds(5), DateTimeOffset.Parse(detail.History.First().SampledAt));
        Assert.AreEqual(7, detail.Latest!.Tasks.Single().TaskId);
        var compact = history.Get(false);
        Assert.AreEqual(0, compact.Latest!.Tasks.Count);
        Assert.AreEqual(0, compact.History.Count);
        Assert.AreEqual(detail.Latest.SampledAt, compact.Latest.SampledAt);
        history.Append(start.AddHours(2), idle, []);
        Assert.AreEqual(1, history.Get(true).History.Count);
    }
}
