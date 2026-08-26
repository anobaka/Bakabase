using System.Collections.Generic;

namespace Bakabase.Service.Models.Input;

public record ProxyTestInputModel
{
    /// <summary>
    /// Id of a saved custom proxy to test. Ignored when <see cref="Address"/> is given.
    /// Both null means test a direct connection, which is how the user compares
    /// "is it the proxy or is it my network?".
    /// </summary>
    public string? CustomProxyId { get; set; }

    /// <summary>
    /// Proxy address to test directly, for trying one before saving it.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Test through the system proxy. Distinct from supplying no proxy at all, which
    /// tests a direct connection and deliberately bypasses any system setting.
    /// </summary>
    public bool UseSystemProxy { get; set; }

    /// <summary>
    /// Preset site ids (see <c>ProxyTestSites</c>) to include. Null falls back to the
    /// user's saved selection, then to the built-in default selection.
    /// </summary>
    public List<string>? PresetSiteIds { get; set; }

    /// <summary>
    /// Additional arbitrary URLs to test. Null falls back to the user's saved custom sites.
    /// </summary>
    public List<string>? CustomSites { get; set; }
}
