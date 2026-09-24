using System.Net;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// What a relay shows when it has no server to show: the server was forgotten on this
/// device, or it no longer accepts this device's key.
/// </summary>
/// <remarks>
/// <para>
/// The thin client's connect page is the wrong screen here — this window is not a client
/// waiting for an address, it is the desktop app, and the way forward is back to its own
/// UI, where servers are added and removed. So the page says what happened and links
/// there, and nothing else.
/// </para>
/// <para>
/// Inline and script-free, with a policy that forbids scripts outright: it runs at a
/// relay's origin, with that relay's <c>/client</c> API in reach, and has no reason to
/// execute anything. Both languages are shown at once for the same reason there is no
/// script to pick one.
/// </para>
/// </remarks>
public static class ConsoleUnavailablePage
{
    public const string ContentSecurityPolicy =
        "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; " +
        "frame-ancestors 'none'";

    /// <param name="localOrigin">This device's own UI; the link is omitted when it is not known.</param>
    public static string Render(string? localOrigin)
    {
        var link = localOrigin != null &&
                   Uri.TryCreate(localOrigin, UriKind.Absolute, out var origin) &&
                   origin.Scheme is "http" or "https"
            ? $"""<p><a href="{WebUtility.HtmlEncode(origin.GetLeftPart(UriPartial.Authority) + "/")}">Back to this device · 返回本机</a></p>"""
            : string.Empty;

        return $$"""
                 <!DOCTYPE html>
                 <html lang="en">
                 <head>
                 <meta charset="utf-8">
                 <meta name="viewport" content="width=device-width, initial-scale=1">
                 <title>Bakabase</title>
                 <style>
                   :root { color-scheme: light dark; }
                   body { margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
                          padding: 32px 20px; font: 14px/1.6 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
                   main { max-width: 520px; }
                   h1 { margin: 0 0 8px; font-size: 20px; font-weight: 600; }
                   p { margin: 0 0 10px; opacity: .85; }
                   a { font-weight: 600; }
                 </style>
                 </head>
                 <body>
                 <main>
                   <h1>This server is not available</h1>
                   <p>This device no longer manages it — it was removed here, or the server stopped accepting this device.
                     Add it again from this device's Devices page.</p>
                   <h1 lang="zh-Hans">该设备当前不可用</h1>
                   <p lang="zh-Hans">本机已不再管理这台设备：它已在本机被移除，或对方不再接受本机。可以在本机的“设备”页面重新添加。</p>
                   {{link}}
                 </main>
                 </body>
                 </html>
                 """;
    }
}
