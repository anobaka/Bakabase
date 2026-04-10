namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

/// <summary>
/// Shared account model for third-party platforms that need cookie-based authentication.
/// </summary>
public class ThirdPartyAccount
{
    public string? Name { get; set; }
    public string? Cookie { get; set; }
    public string? UserAgent { get; set; }
    /// <summary>
    /// HttpCloak TLS fingerprint preset identifier (e.g. "chrome-146", "firefox-133").
    /// Must be consistent with the User-Agent to avoid detection.
    /// </summary>
    public string? TlsPreset { get; set; }
}
