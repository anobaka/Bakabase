using System.Collections.Generic;

namespace Bakabase.Service.Models.View;

/// <summary>
/// State payload returned by <c>GET /app/telemetry-snapshot</c> for the frontend to upload
/// to GA4 as user properties (the strings) and event metrics (the numbers). Designed to
/// hash stably so the frontend can suppress duplicate uploads when nothing has changed
/// between launches (see design §6.5).
///
/// Privacy: every field below is either an enum/version, a count, or a list of stable ids.
/// No paths, file names, resource titles, account info, or anything user-typed.
/// </summary>
public record TelemetrySnapshotViewModel
{
    public string AppVersion { get; init; } = string.Empty;

    /// <summary>One of <c>"stable"</c>, <c>"beta"</c>, <c>"dev"</c>.</summary>
    public string ReleaseChannel { get; init; } = string.Empty;

    /// <summary>Normalised to <c>"windows"</c> / <c>"macos"</c> / <c>"linux"</c> / <c>"other"</c>.</summary>
    public string Os { get; init; } = string.Empty;

    public string Locale { get; init; } = string.Empty;

    public int MediaLibraryCount { get; init; }

    public int ResourceCount { get; init; }

    /// <summary>
    /// Distinct ids of enhancers that the user has configured anywhere in their resource
    /// profiles. Sorted alphabetically so the snapshot hash is stable regardless of profile
    /// ordering.
    /// </summary>
    public List<string> EnabledEnhancers { get; init; } = [];

    public bool AiEnabled { get; init; }

    public bool HasMediaLibrary { get; init; }
}
