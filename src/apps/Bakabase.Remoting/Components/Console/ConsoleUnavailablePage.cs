using System.Net;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// What a relay shows when it has no server to show: the server was forgotten on this
/// device, its address now answers as another server or as this device itself, what answers
/// there has remote access turned off, or nobody answers there at all.
/// </summary>
/// <remarks>
/// <para>
/// This window is not a client waiting for an address, it is the desktop app, and the way
/// forward is back to its own UI, where servers are added and removed. So the page says
/// what happened and links there, and nothing else.
/// </para>
/// <para>
/// Inline and script-free, with a policy that forbids scripts outright: it runs at a
/// relay's origin, with that relay's <c>/client</c> API in reach, and has no reason to
/// execute anything. Both languages are shown at once for the same reason there is no
/// script to pick one. Every name and address on it is encoded — the name of whichever
/// server answered at the address is that server's to choose.
/// </para>
/// </remarks>
public static class ConsoleUnavailablePage
{
    public const string ContentSecurityPolicy =
        "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; " +
        "frame-ancestors 'none'";

    /// <param name="localOrigin">This device's own UI; the link is omitted when it is not known.</param>
    /// <param name="check">
    /// Why nothing was forwarded; null when this device no longer manages the server at all.
    /// </param>
    /// <param name="serverName">What the server this relay is for is called here.</param>
    public static string Render(string? localOrigin, UpstreamIdentityCheck? check = null, string? serverName = null)
    {
        var link = localOrigin != null &&
                   Uri.TryCreate(localOrigin, UriKind.Absolute, out var origin) &&
                   origin.Scheme is "http" or "https"
            ? $"""<p><a href="{E(origin.GetLeftPart(UriPartial.Authority) + "/")}">Back to this device · 返回本机</a></p>"""
            : string.Empty;

        var (title, body, titleZh, bodyZh) = Texts(check, serverName);

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
                   p { margin: 0 0 10px; opacity: .85; overflow-wrap: anywhere; }
                   a { font-weight: 600; }
                 </style>
                 </head>
                 <body>
                 <main>
                   <h1>{{title}}</h1>
                   <p>{{body}}</p>
                   <h1 lang="zh-Hans">{{titleZh}}</h1>
                   <p lang="zh-Hans">{{bodyZh}}</p>
                   {{link}}
                 </main>
                 </body>
                 </html>
                 """;
    }

    /// <summary>Writes the page as the answer to <paramref name="context"/>'s request, uncached.</summary>
    public static Task WriteAsync(HttpContext context, string? localOrigin, UpstreamIdentityCheck? check = null,
        string? serverName = null)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;

        return context.Response.WriteAsync(Render(localOrigin, check, serverName), context.RequestAborted);
    }

    /// <summary>The page's texts, already encoded.</summary>
    private static (string Title, string Body, string TitleZh, string BodyZh) Texts(UpstreamIdentityCheck? check,
        string? serverName)
    {
        if (check == null)
        {
            return ("This server is not available",
                "This device no longer manages it — it was removed here, or the server stopped accepting this " +
                "device. Add it again from this device's Devices page.",
                "该设备当前不可用",
                "本机已不再管理这台设备：它已在本机被移除，或对方不再接受本机。可以在本机的“设备”页面重新添加。");
        }

        var name = E(string.IsNullOrWhiteSpace(serverName) ? check.ServerId : serverName);
        var address = E(check.Authority);
        var other = E(check.AnsweredByName ?? check.AnsweredById ?? "?");

        return check.Verdict switch
        {
            UpstreamIdentityVerdict.WrongServer => (
                $"{name} is not at its address any more",
                $"{address} now answers as another server ({other}), so this window sent it nothing. If {name} " +
                "moved to another address, find it again on this device's Devices page; if it was reinstalled or " +
                "its data was reset, pair with it again there.",
                $"{name} 已不在原来的地址",
                $"{address} 现在是另一台设备（{other}），本窗口没有向它发送任何请求。如果 {name} 换了地址，请在本机的“设备”页面重新找到它；" +
                "如果它重装过或数据被重置，请在那里重新配对。"),
            UpstreamIdentityVerdict.ThisDevice => (
                $"{name} is not at its address any more",
                $"{address} now reaches this device itself, so this window sent it nothing. If {name} moved to " +
                "another address, find it again on this device's Devices page.",
                $"{name} 已不在原来的地址",
                $"{address} 现在指向本机自身，本窗口没有向它发送任何请求。如果 {name} 换了地址，请在本机的“设备”页面重新找到它。"),
            // Something is running there and reachable: telling the user to check that would
            // send them the wrong way. Worded as the refusal a fetch gets (UpstreamIdentityCheck.Describe).
            _ when check.RemoteAccessDisabled => (
                $"Remote access is turned off at {address}",
                $"The Bakabase at {address} has remote access turned off, so it cannot confirm that it is {name}, " +
                "and this window sent it nothing. Turn it on in Bakabase on that device, under “Let other devices " +
                "manage this device”, then reload this page.",
                $"{address} 已关闭远程访问",
                $"{address} 上的 Bakabase 已关闭远程访问，无法确认它就是 {name}，本窗口没有向它发送任何请求。请在那台设备的 Bakabase 中" +
                "开启“允许其他设备管理本机”，然后刷新本页。"),
            _ => (
                $"{name} is not answering",
                $"Nothing that could be identified as {name} answers at {address}. Check that Bakabase is running " +
                "there and that this device can reach it, then reload this page.",
                $"{name} 没有响应",
                $"{address} 上没有可以确认为 {name} 的设备响应。请确认那台设备上的 Bakabase 正在运行、本机可以访问它，然后刷新本页。")
        };
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}

/// <summary>The console's page for a relay that forwards nothing, as the relay core asks for it.</summary>
public sealed class ConsoleUnavailablePageWriter(ConsoleRelayContext relay) : IRelayUnavailablePage
{
    public Task WriteAsync(HttpContext context, UpstreamIdentityCheck check, string? serverName) =>
        ConsoleUnavailablePage.WriteAsync(context, relay.Navigator.LocalOrigin, check, serverName);
}
