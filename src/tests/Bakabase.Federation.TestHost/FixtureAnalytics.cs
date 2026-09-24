using Microsoft.Extensions.Configuration;

/// <summary>
/// Keeps a test host from reporting to the real analytics projects. The shipped
/// <c>appsettings.json</c> and <c>client-analytics.json</c> carry live project ids and DSNs,
/// and anonymous tracking is on by default; a fixture serves the production frontend, which
/// reads both from <c>/app/analytics-info</c> and starts Clarity, GA4, PostHog and Sentry with
/// them. Shared by both test hosts (the thin client's links this file).
/// </summary>
static class FixtureAnalytics
{
    /// <summary>What the Service and the thin client read, whether or not a shipped file sets it.</summary>
    private static readonly string[] Keys =
    [
        "Analytics:Clarity:ProjectId", "Analytics:Ga4:MeasurementId", "Analytics:Sentry:FrontendDsn",
        "Analytics:Sentry:BackendDsn", "Analytics:Sentry:ClientDsn", "Analytics:PostHog:ApiKey",
        "Analytics:PostHog:ApiHost"
    ];

    /// <summary>
    /// Blanks every analytics key — the known ones and whatever the shipped settings files put
    /// under <c>Analytics</c>, so a tracker added there later is blanked too — and turns
    /// anonymous tracking off. Environment variables, so they win over those files.
    /// </summary>
    public static void TurnOff()
    {
        var shipped = new ConfigurationBuilder()
            .SetBasePath(System.AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("client-analytics.json", optional: true)
            .Build();
        var keys = Keys.Concat(shipped.GetSection("Analytics").AsEnumerable()
            .Where(pair => !string.IsNullOrEmpty(pair.Value)).Select(pair => pair.Key));
        foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable(key.Replace(":", "__"), "");
        Environment.SetEnvironmentVariable("App__EnableAnonymousDataTracking", "false");
    }

    /// <summary>
    /// Refuses to report ready while anything could still reach an analytics project: a
    /// non-empty key under <c>Analytics</c> in the host's effective configuration, or — where
    /// the host serves the UI — anonymous tracking left on.
    /// </summary>
    public static void Verify(IConfiguration configuration, bool anonymousTracking = false)
    {
        var set = configuration.GetSection("Analytics").AsEnumerable()
            .Where(pair => !string.IsNullOrEmpty(pair.Value)).Select(pair => pair.Key).ToList();
        if (anonymousTracking)
            set.Add("App:EnableAnonymousDataTracking");
        if (set.Count > 0)
            throw new InvalidOperationException("The fixture would report to analytics: " + string.Join(", ", set));
    }
}
