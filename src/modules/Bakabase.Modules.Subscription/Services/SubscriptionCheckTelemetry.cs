using System.Diagnostics;
using Bakabase.Abstractions.Components.Telemetry;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Subscription.Services;

/// <summary>One summary per check; counts describe source items, never their titles or identifiers.</summary>
internal sealed class SubscriptionCheckTelemetry(IServiceProvider services, CancellationToken cancellationToken) : IDisposable
{
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private string? _outcome;
    public SubscriptionSourceKind? SourceKind { get; set; }
    public int? ItemCount { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Existing { get; set; }
    public int Added { get; set; }

    // A check is complete once collection membership and LastCheckedAt have been saved. Notifications
    // and optional acquisition are separate operations and cannot turn this into another check.
    public void Complete()
    {
        _elapsed.Stop();
        _outcome = Failed == 0 ? "success" : Succeeded > 0 ? "partial_success" : "failed";
    }

    public void Dispose() => TelemetryCapture.TryTrack(services, "subscription_check_completed", new Dictionary<string, object?>
    {
        ["feature_id"] = "subscriptions",
        ["subscription_source_kind"] = SourceKind is { } kind && Enum.IsDefined(kind) ? kind.ToString() : "unknown",
        ["outcome"] = _outcome ?? (cancellationToken.IsCancellationRequested ? "cancelled" : "failed"),
        ["duration_ms"] = _elapsed.Elapsed.TotalMilliseconds,
        ["attempt_id"] = Guid.NewGuid().ToString("D"),
        ["item_count"] = ItemCount, ["success_count"] = Succeeded, ["failure_count"] = Failed,
        ["skipped_count"] = Existing, ["new_item_count"] = Added
    });
}
