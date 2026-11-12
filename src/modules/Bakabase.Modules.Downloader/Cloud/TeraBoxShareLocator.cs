using System.Text.RegularExpressions;

namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Reviewed public share shapes. Canonicalizing a host never authorizes an arbitrary URL.</summary>
public sealed record TeraBoxShareLocator(string ShareId, string? Password)
{
    public string CanonicalUrl => "https://www.terabox.com/s/" + ShareId;

    public static bool TryParse(string? value, out TeraBoxShareLocator? locator)
    {
        locator = null;
        if (value == null || value.Length > 2048 || value.Any(char.IsControl) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            uri.IdnHost.ToLowerInvariant() is not ("terabox.com" or "www.terabox.com" or
                "1024terabox.com" or "www.1024terabox.com")) return false;
        var match = Regex.Match(uri.AbsolutePath, @"\A/s/(1[A-Za-z0-9_-]{5,159})/?\z");
        if (!match.Success) return false;
        string? password = null;
        foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = parameter.Split('=', 2);
            if (!Uri.UnescapeDataString(pair[0]).Equals("pwd", StringComparison.Ordinal)) continue;
            var candidate = Uri.UnescapeDataString(pair.ElementAtOrDefault(1) ?? "");
            if (candidate.Length > 128 || candidate.Any(char.IsControl) ||
                password != null && password != candidate) return false;
            password = candidate;
        }
        locator = new(match.Groups[1].Value, string.IsNullOrEmpty(password) ? null : password);
        return true;
    }
}
