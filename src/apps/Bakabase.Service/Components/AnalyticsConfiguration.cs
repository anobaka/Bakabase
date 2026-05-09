namespace Bakabase.Service.Components;

/// <summary>
/// Bound from the <c>Analytics</c> section of <c>appsettings.json</c>. The shared defaults
/// shipped to git make every install report into the same aggregate dashboards; forks
/// override via environment variables (<c>Analytics__Clarity__ProjectId</c>,
/// <c>Analytics__Ga4__MeasurementId</c>, <c>Analytics__Sentry__Dsn</c>) or a
/// per-deployment <c>appsettings.{env}.json</c>.
/// </summary>
public class AnalyticsConfiguration
{
    public ClarityOptions Clarity { get; set; } = new();
    public Ga4Options Ga4 { get; set; } = new();
    public SentryOptions Sentry { get; set; } = new();
    public PostHogOptions PostHog { get; set; } = new();

    public class ClarityOptions
    {
        public string? ProjectId { get; set; }
    }

    public class Ga4Options
    {
        public string? MeasurementId { get; set; }
    }

    public class SentryOptions
    {
        /// <summary>The DSN exposed to the frontend for browser-side Sentry init.</summary>
        public string? FrontendDsn { get; set; }

        /// <summary>The DSN consumed by the .NET host. Usually different from <see cref="FrontendDsn"/>
        /// because we want frontend and backend issues in separate Sentry projects (per design
        /// decision §16, item 2).</summary>
        public string? BackendDsn { get; set; }
    }

    /// <summary>
    /// PostHog runs alongside GA4 (per the dual-analytics decision). Same set of user
    /// properties / events get mirrored to both. Useful as a fallback when GA4 is broken
    /// or unreachable, and as a primary for users in regions where GA4 is blocked.
    /// </summary>
    public class PostHogOptions
    {
        /// <summary>The PostHog project API key (a.k.a. "Project API Key", starts with
        /// <c>phc_</c>). Surfaced to the frontend; safe to expose since it's write-only.</summary>
        public string? ApiKey { get; set; }

        /// <summary>PostHog ingestion endpoint. Cloud values: <c>https://us.i.posthog.com</c>
        /// (default) or <c>https://eu.i.posthog.com</c>. Self-hosted: your own host.</summary>
        public string ApiHost { get; set; } = "https://us.i.posthog.com";
    }
}
