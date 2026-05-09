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
}
