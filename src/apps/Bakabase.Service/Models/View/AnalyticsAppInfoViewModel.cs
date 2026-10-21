namespace Bakabase.Service.Models.View;

/// <summary>
/// Bootstrapping payload returned by <c>GET /app/analytics-info</c>. Carries everything
/// the frontend needs to decide whether to initialise analytics SDKs and, if so, with
/// which project ids. Per-app statistics live on the separate
/// <c>GET /app/telemetry-snapshot</c> endpoint.
/// </summary>
public record AnalyticsAppInfoViewModel
{
    /// <summary>Mirror of <c>AppOptions.EnableAnonymousDataTracking</c>; gates all SDK init.</summary>
    public bool EnableAnonymousDataTracking { get; init; }

    /// <summary>Legacy compatibility alias for DeploymentId. Persisted in effective AppData;
    /// never use it as the v2 client identity.</summary>
    public string DeviceId { get; init; } = string.Empty;

    public string DeploymentId { get; init; } = string.Empty;
    public string DeploymentType { get; init; } = string.Empty;
    public string ServerOs { get; init; } = string.Empty;
    public string ServerArch { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public int SchemaVersion { get; init; } = 2;
    public int IdentityVersion { get; init; } = 2;
    public bool EnableSessionReplay { get; init; }

    /// <summary>The running app version. Surfaced here (rather than only on the telemetry
    /// snapshot) because Sentry needs <c>release</c> at init time, before the first error
    /// is captured.</summary>
    public string AppVersion { get; init; } = string.Empty;

    /// <summary>One of <c>"stable"</c>, <c>"beta"</c>, <c>"dev"</c>. Derived server-side
    /// from the SemVer prerelease suffix; used as a GA4 user property and Sentry tag.</summary>
    public string ReleaseChannel { get; init; } = string.Empty;

    public string? ClarityProjectId { get; init; }
    public string? Ga4MeasurementId { get; init; }

    /// <summary>Sentry DSN for the frontend project. Distinct from the backend DSN.</summary>
    public string? SentryDsn { get; init; }

    /// <summary>PostHog project API key (starts with <c>phc_</c>). Runs alongside GA4 —
    /// same events / user properties get mirrored to both.</summary>
    public string? PostHogApiKey { get; init; }

    /// <summary>PostHog ingestion endpoint (cloud or self-hosted). Always non-null when
    /// <see cref="PostHogApiKey"/> is set; defaults to US cloud otherwise.</summary>
    public string PostHogApiHost { get; init; } = string.Empty;
}
