namespace Bakabase.Abstractions.Components.Gui;

/// <summary>
/// Carries only the current page's path to the destination UI. That UI resolves its own
/// visible menu ancestor; resource IDs, filters and device-specific parameters are never
/// used directly as the destination route. Older UIs fall back from this unknown route.
/// </summary>
public static class DeviceSwitchNavigation
{
    public static string ForCurrentMenu(string targetUrl, string? currentUrl)
    {
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var target) ||
            target.Scheme is not ("http" or "https"))
            throw new ArgumentException("A device switch requires an absolute HTTP(S) destination.", nameof(targetUrl));

        var path = "/";
        if (Uri.TryCreate(currentUrl, UriKind.Absolute, out var current) &&
            current.Scheme is "http" or "https" && current.Fragment.StartsWith("#/", StringComparison.Ordinal))
        {
            var candidate = current.Fragment[1..].Split(['?', '#'], 2)[0];
            if (candidate.Length <= 512 && !candidate.Contains("//", StringComparison.Ordinal) &&
                candidate != "/device-switch" && !candidate.StartsWith("/device-switch/", StringComparison.Ordinal) &&
                candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-'))
                path = candidate;
        }

        // The relay's one-time ticket belongs before the fragment. Preserve its query
        // verbatim; the existing landing page forwards this fragment after spending it.
        return targetUrl.Split('#')[0] + "#/device-switch?path=" + Uri.EscapeDataString(path);
    }
}
