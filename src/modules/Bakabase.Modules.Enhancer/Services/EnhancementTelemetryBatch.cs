using System.Diagnostics;
using Bakabase.Abstractions.Components.Telemetry;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Services;

/// <summary>One summary per provider per batch, after persistence; no resource identifiers or content.</summary>
public sealed class EnhancementTelemetryBatch(IServiceProvider services, IEnumerable<int> enhancerIds,
    CancellationToken cancellationToken) : IDisposable
{
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private readonly Dictionary<int, Counts> _counts = enhancerIds.GroupBy(id => id)
        .ToDictionary(group => group.Key, group => new Counts { Total = group.Count() });
    public bool Completed { get; set; }

    public void Persisted(int enhancerId, bool failed, bool empty)
    {
        var count = _counts[enhancerId];
        if (failed) Interlocked.Increment(ref count.Failed);
        else Interlocked.Increment(ref count.Succeeded);
        if (empty) Interlocked.Increment(ref count.Empty);
    }

    public void Skipped(int enhancerId) => Interlocked.Increment(ref _counts[enhancerId].Skipped);

    public void Dispose()
    {
        foreach (var (id, count) in _counts)
        {
            var incomplete = Math.Max(0, count.Total - count.Succeeded - count.Failed - count.Skipped);
            var outcome = incomplete > 0 ? cancellationToken.IsCancellationRequested ? "cancelled" : "failed" :
                count.Skipped == count.Total ? "skipped" : count.Failed > 0 ?
                    count.Succeeded > 0 ? "partial_success" : "failed" : Completed ? "success" : "failed";
            TelemetryCapture.TryTrack(services, "enhancement_completed", new Dictionary<string, object?>
            {
                ["feature_id"] = "enhancer",
                ["enhancer_id"] = Enum.IsDefined(typeof(EnhancerId), id) ? ((EnhancerId) id).ToString() : "unknown",
                ["outcome"] = outcome, ["item_count"] = count.Total, ["success_count"] = count.Succeeded,
                ["failure_count"] = count.Failed, ["skipped_count"] = count.Skipped,
                ["incomplete_count"] = incomplete, ["zero_result_count"] = count.Empty,
                ["duration_ms"] = _timer.Elapsed.TotalMilliseconds
            });
        }
    }

    private sealed class Counts
    {
        public int Total;
        public int Succeeded;
        public int Failed;
        public int Skipped;
        public int Empty;
    }
}
