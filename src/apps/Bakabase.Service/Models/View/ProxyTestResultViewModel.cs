namespace Bakabase.Service.Models.View;

public record ProxyTestResultViewModel
{
    /// <summary>Preset site id, or the URL itself for a custom site.</summary>
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string Url { get; set; } = null!;

    /// <summary>
    /// True when the destination answered. A non-2xx status still counts: it proves the
    /// request reached the site, which is what a connectivity test is asking.
    /// </summary>
    public bool Succeeded { get; set; }

    public int? StatusCode { get; set; }
    public int ElapsedMs { get; set; }
    public string? Error { get; set; }
}
