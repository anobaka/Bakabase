using System.Text;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>Why an address typed for another device cannot be used.</summary>
public enum RemoteAddressProblem
{
    None = 0,

    /// <summary>
    /// Not an http or https host and port: nothing typed, a path, a query, credentials, or a
    /// host name no name lookup could take.
    /// </summary>
    Invalid = 1,

    /// <summary>
    /// A bare host, typed without a scheme or a port. Nothing can be guessed for it: the
    /// desktop app picks its port at launch (34567 when free, the next ones when not) and a
    /// Docker server listens on whatever it was given (8080 by default).
    /// </summary>
    PortMissing = 2
}

/// <summary>
/// One reading of the address a person types for another device — to manage it, or to browse
/// its library — so both features accept and refuse the same things.
/// </summary>
/// <remarks>
/// <para>
/// A Chinese input method types <c>：．。</c> and full-width digits unless switched to
/// half-width, and a Windows user reaches for <c>\\PC1</c>; both become the address they
/// mean. Left as they were, <see cref="Uri"/> takes a full-width colon for part of an
/// international host name, and the request fails later with a <see cref="UriFormatException"/>
/// nothing expects.
/// </para>
/// <para>
/// An address typed without a scheme must say its port: there is no default to fall back on
/// (see <see cref="RemoteAddressProblem.PortMissing"/>), and HTTP's own, 80, is where no
/// Bakabase listens — the attempt only ended in "nothing answered". One with <c>http://</c>
/// or <c>https://</c> keeps its scheme's port, as a URL does, so a server behind a reverse
/// proxy (<c>https://bakabase.example.com</c>) still works.
/// </para>
/// </remarks>
public static class RemoteAddressInput
{
    /// <summary>
    /// What was typed, with full-width characters made ASCII, blanks, leading backslashes and
    /// trailing slashes gone, and <c>http://</c> in front when it names no scheme. An address
    /// already in that form comes back unchanged, so a stored address and a freshly typed one
    /// compare equal.
    /// </summary>
    public static string Normalize(string? input)
    {
        var text = Clean(input);

        return text.Length == 0 || HasScheme(text) ? text : $"http://{text}";
    }

    /// <summary>
    /// Reads <paramref name="input"/> as another device's address. <paramref name="root"/> is
    /// its origin, <c>scheme://host:port/</c>, when there is no problem.
    /// </summary>
    public static RemoteAddressProblem Parse(string? input, out Uri? root)
    {
        root = null;
        var text = Clean(input);

        if (text.Length == 0 ||
            !Uri.TryCreate(HasScheme(text) ? text : $"http://{text}", UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !IsHostName(uri))
        {
            return RemoteAddressProblem.Invalid;
        }

        if (!HasScheme(text) && !NamesPort(text))
        {
            return RemoteAddressProblem.PortMissing;
        }

        root = uri;
        return RemoteAddressProblem.None;
    }

    private static string Clean(string? input)
    {
        var text = new StringBuilder(input?.Length ?? 0);

        foreach (var c in input ?? string.Empty)
        {
            text.Append(c switch
            {
                // The full-width forms of ASCII (：．／０-９ and the rest) and the ideographic
                // full stop, which name lookup reads as a dot too.
                >= '\uFF01' and <= '\uFF5E' => (char) (c - 0xFEE0),
                '\u3002' or '\uFF61' => '.',
                _ => c
            });
        }

        return text.ToString().Trim().TrimStart('\\').TrimEnd('/', '\\');
    }

    private static bool HasScheme(string text) => text.Contains("://", StringComparison.Ordinal);

    /// <summary>
    /// Whether the host is one a connection can be opened to. A host .NET cannot turn into
    /// its ASCII form throws only when a request is sent.
    /// </summary>
    private static bool IsHostName(Uri uri)
    {
        try
        {
            return Uri.CheckHostName(uri.IdnHost) is UriHostNameType.Dns or UriHostNameType.IPv4
                or UriHostNameType.IPv6;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    /// <summary>Whether the authority, <c>host:port</c> or <c>[v6]:port</c>, ends in a port.</summary>
    private static bool NamesPort(string text)
    {
        var authority = text.Split('/', '?', '#')[0];
        var colon = authority.LastIndexOf(':');

        return colon > authority.LastIndexOf(']') && colon < authority.Length - 1;
    }
}
