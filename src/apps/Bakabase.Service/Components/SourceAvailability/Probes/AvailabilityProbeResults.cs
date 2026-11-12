using System;
using System.Collections.Generic;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.SourceAvailability.Probes;

internal static class AvailabilityProbeResults
{
    public static SourceAvailabilityResult Create(string probe, string status, string reason,
        Dictionary<string, string>? evidence = null, int version = 1)
    {
        var now = DateTimeOffset.UtcNow;
        return new() { Status = status, ReasonCode = reason, ProbeId = probe, ProbeVersion = version,
            Evidence = evidence ?? [], CheckedAt = now,
            ExpiresAt = now.Add(status switch
            {
                SourceAvailabilityStatuses.Available => TimeSpan.FromMinutes(30),
                SourceAvailabilityStatuses.ConfirmedUnavailable => TimeSpan.FromHours(6),
                SourceAvailabilityStatuses.TemporarilyUnavailable => TimeSpan.FromMinutes(2),
                _ => TimeSpan.FromMinutes(10)
            }) };
    }

    // HttpClient may wrap the public-DNS boundary exception. Inspect types, never
    // remote message text; this is a setup requirement, not share invalidity.
    public static bool RequiresExplicitNetworkConfiguration(Exception error)
    {
        Exception? current = error;
        for (var depth = 0; depth < 16 && current != null; depth++, current = current.InnerException)
            if (current is CloudSourceNetworkException) return true;
        return false;
    }
}
