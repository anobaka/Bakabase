using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// The only thing standing between a local port and every page the user has open.
/// </summary>
/// <remarks>
/// The forwarding layer has no authentication of its own — reaching it is reaching the
/// user's whole library, signed with the device key — so this is where a mistake is
/// expensive, and where the tests have to be specific about what an attack looks like.
/// </remarks>
[TestClass]
public class LoopbackOriginGuardTests
{
    private const int Port = 34568;
    private static readonly LoopbackOriginGuard Guard = new(Port);

    private static LoopbackGuardVerdict Get(string? host, string? origin = null) =>
        Guard.Evaluate(host, origin, "GET");

    private static LoopbackGuardVerdict Post(string? host, string? origin = null) =>
        Guard.Evaluate(host, origin, "POST");

    [TestMethod]
    public void The_clients_own_window_is_served()
    {
        Assert.AreEqual(LoopbackGuardVerdict.Allowed, Get($"127.0.0.1:{Port}"));
        Assert.AreEqual(LoopbackGuardVerdict.Allowed, Get($"localhost:{Port}"));
        Assert.AreEqual(LoopbackGuardVerdict.Allowed,
            Post($"127.0.0.1:{Port}", $"http://127.0.0.1:{Port}"));
        Assert.AreEqual(LoopbackGuardVerdict.Allowed,
            Post($"localhost:{Port}", $"http://localhost:{Port}"));
    }

    [TestMethod]
    public void A_rebound_hostname_is_refused()
    {
        // DNS rebinding: evil.com resolves to 127.0.0.1, so the connection genuinely
        // arrives here — but the browser sends the hostname from the URL bar, and that
        // is still evil.com. This check is the whole defence.
        foreach (var host in new[] {$"evil.com:{Port}", $"bakabase.evil.com:{Port}", $"192.168.1.5:{Port}"})
        {
            Assert.AreEqual(LoopbackGuardVerdict.ForeignHost, Get(host), host);
        }
    }

    [TestMethod]
    public void Another_port_on_this_machine_is_refused()
    {
        // A different local listener's origin is not ours, and a page served by one has
        // no business reaching this.
        Assert.AreEqual(LoopbackGuardVerdict.ForeignHost, Get($"127.0.0.1:{Port + 1}"));
        Assert.AreEqual(LoopbackGuardVerdict.ForeignHost, Get("127.0.0.1"));
    }

    [TestMethod]
    public void A_missing_host_is_refused()
    {
        // HTTP/1.1 requires the header and HTTP/2 requires :authority; something that
        // sends neither is not the embedded browser.
        Assert.AreEqual(LoopbackGuardVerdict.ForeignHost, Get(null));
        Assert.AreEqual(LoopbackGuardVerdict.ForeignHost, Get(""));
    }

    [TestMethod]
    public void A_hostname_that_merely_contains_a_loopback_name_is_refused()
    {
        // The kind of near-miss a prefix or substring check would let through.
        foreach (var host in new[]
                 {
                     $"127.0.0.1.evil.com:{Port}", $"localhost.evil.com:{Port}",
                     $"notlocalhost:{Port}", $"127.0.0.10:{Port}"
                 })
        {
            Assert.AreEqual(LoopbackGuardVerdict.ForeignHost, Get(host), host);
        }
    }

    [TestMethod]
    public void A_cross_site_write_is_refused_even_with_an_honest_host()
    {
        // A page on evil.com can POST to http://127.0.0.1:34568 directly — no rebinding
        // needed, and the Host is truthful. The browser will not hand it the response,
        // but a delete that already happened did not need one.
        Assert.AreEqual(LoopbackGuardVerdict.ForeignOrigin, Post($"127.0.0.1:{Port}", "https://evil.com"));
        Assert.AreEqual(LoopbackGuardVerdict.ForeignOrigin,
            Post($"127.0.0.1:{Port}", $"http://127.0.0.1:{Port + 1}"));
        Assert.AreEqual(LoopbackGuardVerdict.ForeignOrigin, Post($"127.0.0.1:{Port}", "null"));
    }

    [TestMethod]
    public void Every_state_changing_method_is_covered()
    {
        foreach (var method in new[] {"POST", "PUT", "PATCH", "DELETE"})
        {
            Assert.AreEqual(LoopbackGuardVerdict.ForeignOrigin,
                Guard.Evaluate($"127.0.0.1:{Port}", "https://evil.com", method), method);
        }
    }

    [TestMethod]
    public void Without_fetch_metadata_a_cross_site_read_cannot_be_told_apart()
    {
        // A GET from an <img> or a <script> carries no Origin, and an honest Host, so an
        // engine that sends no fetch metadata leaves nothing to tell it by. That is the
        // limit of the old rules, kept for the callers that send none — local players,
        // scripts. A rebound one is still caught by its Host.
        Assert.AreEqual(LoopbackGuardVerdict.Allowed, Get($"127.0.0.1:{Port}", "https://evil.com"));
        Assert.AreEqual(LoopbackGuardVerdict.ForeignHost, Get($"evil.com:{Port}", "https://evil.com"));

        // Every engine that does send it says so outright, and that is refused.
        Assert.AreEqual(LoopbackGuardVerdict.ForeignSite,
            Guard.Evaluate(new LoopbackGuardRequest("GET", $"127.0.0.1:{Port}", null, "cross-site", "no-cors",
                "image", "/tool/open?path=%2Fdata")).Verdict);
    }

    [TestMethod]
    public void A_write_with_no_origin_at_all_is_allowed()
    {
        // Browsers always send Origin on a state-changing request. What omits it is a
        // local player fetching a stream, or a script the user ran themselves — neither
        // is the vector this guards, and refusing them would break playback.
        Assert.AreEqual(LoopbackGuardVerdict.Allowed, Post($"127.0.0.1:{Port}"));
        Assert.AreEqual(LoopbackGuardVerdict.Allowed, Post($"127.0.0.1:{Port}", ""));
    }

    [TestMethod]
    public void Case_does_not_decide_anything()
    {
        Assert.AreEqual(LoopbackGuardVerdict.Allowed, Get($"LOCALHOST:{Port}"));
        Assert.AreEqual(LoopbackGuardVerdict.Allowed, Post($"127.0.0.1:{Port}", $"HTTP://LOCALHOST:{Port}"));
    }

    // ---- fetch metadata and switch tickets ----

    private const string Ticket = RelayNavigationTokens.QueryName;
    private static readonly string OurHost = $"127.0.0.1:{Port}";
    private static readonly string OurOrigin = $"http://127.0.0.1:{Port}";

    public enum TicketKind
    {
        None,
        Valid,
        Invalid,
        Used,
        WrongPort,
        Expired
    }

    private sealed record Armed(LoopbackOriginGuard Guard, RelayNavigationTokens Tickets,
        RelayNavigationTokensTests.ManualClock Clock);

    private static Armed Arm()
    {
        var clock = new RelayNavigationTokensTests.ManualClock();
        var tickets = new RelayNavigationTokens(clock);

        return new Armed(new LoopbackOriginGuard(Port, tickets), tickets, clock);
    }

    /// <summary>A ticket in the state <paramref name="kind"/> names, or null for none at all.</summary>
    private static string? Prepare(Armed armed, TicketKind kind)
    {
        switch (kind)
        {
            case TicketKind.None:
                return null;
            case TicketKind.Valid:
                return armed.Tickets.Mint(Port);
            case TicketKind.Invalid:
                return new string('a', 32);
            case TicketKind.Used:
            {
                var ticket = armed.Tickets.Mint(Port);
                Assert.IsTrue(armed.Tickets.TryConsume(ticket, Port));
                return ticket;
            }
            case TicketKind.WrongPort:
                return armed.Tickets.Mint(Port + 1);
            case TicketKind.Expired:
            {
                var ticket = armed.Tickets.Mint(Port);
                armed.Clock.Advance(RelayNavigationTokens.Lifetime + TimeSpan.FromSeconds(1));
                return ticket;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>Where a window switching servers would ask to land, with the ticket in the middle.</summary>
    private static string Target(string? ticket) =>
        ticket == null ? "/resource/42?tab=files&q=a%20b" : $"/resource/42?tab=files&{Ticket}={ticket}&q=a%20b";

    private const string TargetWithoutTicket = "/resource/42?tab=files&q=a%20b";

    private static LoopbackGuardRequest Navigation(string? site, string target, string method = "GET",
        string mode = "navigate", string dest = "document", string? origin = null, string? host = null) =>
        new(method, host ?? OurHost, origin, site, mode, dest, target);

    [TestMethod]
    public void The_full_matrix()
    {
        // Every combination of who the browser says is asking, how, and with what ticket.
        // The rule it pins: a request another site made is refused unless it is a window
        // arriving with a good ticket for this relay; everything else is judged by the
        // old rules, and a ticket on an admitted GET is spent and taken out of the address.
        var sites = new string?[] {null, "same-origin", "none", "same-site", "cross-site"};
        var modes = new[] {("navigate", "document"), ("cors", "empty"), ("no-cors", "image")};

        foreach (var site in sites)
        foreach (var method in new[] {"GET", "POST"})
        foreach (var (mode, dest) in modes)
        foreach (var kind in Enum.GetValues<TicketKind>())
        {
            var armed = Arm();
            var ticket = Prepare(armed, kind);
            var foreign = site is "same-site" or "cross-site";

            // What that page would honestly put in Origin on a write.
            var origin = method == "GET"
                ? null
                : site switch
                {
                    "same-origin" => OurOrigin,
                    "same-site" => $"http://127.0.0.1:{Port + 1}",
                    "cross-site" => "https://evil.example",
                    _ => null
                };

            var decision = armed.Guard.Evaluate(
                Navigation(site, Target(ticket), method, mode, dest, origin));
            var label = $"site={site ?? "(absent)"} method={method} mode={mode} ticket={kind}";

            var admitsSwitch = foreign && kind == TicketKind.Valid && method == "GET" && mode == "navigate";
            var dropsTicket = !foreign && ticket != null && method == "GET";

            var expected = admitsSwitch || dropsTicket
                ? LoopbackGuardVerdict.DropTicket
                : foreign
                    ? LoopbackGuardVerdict.ForeignSite
                    : LoopbackGuardVerdict.Allowed;

            Assert.AreEqual(expected, decision.Verdict, label);
            Assert.AreEqual(ticket != null, decision.CarriedTicket, label);

            if (expected == LoopbackGuardVerdict.DropTicket)
            {
                Assert.AreEqual(TargetWithoutTicket, decision.ContinueTo, label);
            }

            if (kind == TicketKind.Valid)
            {
                // Spent exactly when the guard acted on it: admitted a window with it, or
                // took it out of an address it served anyway. A subresource or a write
                // that carried it leaves it for the navigation it was minted for.
                Assert.AreEqual(expected != LoopbackGuardVerdict.DropTicket, armed.Tickets.TryConsume(ticket, Port),
                    $"{label}: ticket spent?");
            }
        }
    }

    [TestMethod]
    public void A_switch_drops_only_the_ticket_from_the_address()
    {
        // Every other parameter keeps its order, its escaping, its duplicates and its
        // emptiness — the window lands where it was sent, minus the ticket.
        var cases = new (string Before, string After)[]
        {
            ("/?{0}", "/"),
            ("/resource/42?{0}", "/resource/42"),
            ("/resource/42?{0}&tab=files", "/resource/42?tab=files"),
            ("/a%2Fb/c%20d?x=1&{0}&y=%26&y=2&z", "/a%2Fb/c%20d?x=1&y=%26&y=2&z"),
            ("/p?q=a+b&{0}&keep=%5F%5Fbakabase_switch", "/p?q=a+b&keep=%5F%5Fbakabase_switch"),
            ("/p?a=1&&{0}", "/p?a=1&"),
            ("//evil.example/x?{0}", "//evil.example/x")
        };

        foreach (var (before, after) in cases)
        {
            var armed = Arm();
            var ticket = armed.Tickets.Mint(Port);
            var target = string.Format(before, $"{Ticket}={ticket}");

            var decision = armed.Guard.Evaluate(Navigation("cross-site", target));

            Assert.AreEqual(LoopbackGuardVerdict.DropTicket, decision.Verdict, target);
            Assert.AreEqual(after, decision.ContinueTo, target);
        }
    }

    [TestMethod]
    public void A_ticket_is_a_ticket_however_its_name_is_spelled()
    {
        // ASP.NET reads query names decoded and ignoring case, so anything that could
        // read a ticket out of the address must also be taken out of it.
        foreach (var name in new[] {"%5F%5Fbakabase_switch", "__BAKABASE_SWITCH", "__bakabase%5Fswitch"})
        {
            var armed = Arm();
            var ticket = armed.Tickets.Mint(Port);

            var decision = armed.Guard.Evaluate(Navigation("cross-site", $"/p?a=1&{name}={ticket}"));

            Assert.AreEqual(LoopbackGuardVerdict.DropTicket, decision.Verdict, name);
            Assert.AreEqual("/p?a=1", decision.ContinueTo, name);
        }
    }

    [TestMethod]
    public void A_ticket_buys_nothing_but_a_top_level_navigation()
    {
        var armed = Arm();
        var ticket = armed.Tickets.Mint(Port);
        var target = Target(ticket);

        foreach (var (request, what) in new[]
                 {
                     (Navigation("cross-site", target, dest: "iframe"), "a frame"),
                     (Navigation("cross-site", target, dest: "embed"), "an embed"),
                     (Navigation("cross-site", target, dest: ""), "no destination"),
                     (Navigation("cross-site", target, mode: "", dest: "document"), "no mode"),
                     (Navigation("cross-site", target, "HEAD"), "a HEAD"),
                     (Navigation("cross-site", target, "OPTIONS", "cors", "empty"), "a preflight"),
                     (Navigation("cross-site", target, "POST", origin: "https://evil.example"), "a form post"),
                     (Navigation("same-site", target, "POST", origin: $"http://127.0.0.1:{Port + 1}"), "a same-site form post"),
                     (Navigation("cross-site", target, mode: "no-cors", dest: "image"), "an image"),
                     (Navigation("cross-site", target, mode: "cors", dest: "empty"), "a fetch"),
                     (Navigation("cross-site", target, mode: "no-cors", dest: "script"), "a script")
                 })
        {
            Assert.AreEqual(LoopbackGuardVerdict.ForeignSite, armed.Guard.Evaluate(request).Verdict, what);
        }

        // None of them spent it.
        Assert.AreEqual(LoopbackGuardVerdict.DropTicket, armed.Guard.Evaluate(Navigation("cross-site", target)).Verdict);
    }

    [TestMethod]
    public void A_ticket_admits_one_window_once()
    {
        var armed = Arm();
        var target = Target(armed.Tickets.Mint(Port));

        Assert.AreEqual(LoopbackGuardVerdict.DropTicket, armed.Guard.Evaluate(Navigation("same-site", target)).Verdict);
        Assert.AreEqual(LoopbackGuardVerdict.ForeignSite, armed.Guard.Evaluate(Navigation("same-site", target)).Verdict);
    }

    [TestMethod]
    public void The_address_a_ticket_was_taken_from_is_not_itself_a_way_in()
    {
        // The window lands there from a page on this origin, so it arrives same-origin.
        // The same address arriving cross-site is just another cross-site navigation.
        var armed = Arm();
        var decision = armed.Guard.Evaluate(Navigation("cross-site", Target(armed.Tickets.Mint(Port))));

        Assert.AreEqual(LoopbackGuardVerdict.DropTicket, decision.Verdict);
        Assert.AreEqual(LoopbackGuardVerdict.ForeignSite,
            armed.Guard.Evaluate(Navigation("cross-site", decision.ContinueTo!)).Verdict);
        Assert.AreEqual(LoopbackGuardVerdict.Allowed,
            armed.Guard.Evaluate(Navigation("same-origin", decision.ContinueTo!)).Verdict);
    }

    [TestMethod]
    public void Two_tickets_in_one_address_admit_nothing_and_spend_nothing()
    {
        var armed = Arm();
        var first = armed.Tickets.Mint(Port);
        var second = armed.Tickets.Mint(Port);

        var decision = armed.Guard.Evaluate(Navigation("cross-site", $"/?{Ticket}={first}&{Ticket}={second}"));

        Assert.AreEqual(LoopbackGuardVerdict.ForeignSite, decision.Verdict);
        Assert.IsTrue(armed.Tickets.TryConsume(first, Port));
        Assert.IsTrue(armed.Tickets.TryConsume(second, Port));
    }

    [TestMethod]
    public void A_rebound_host_cannot_spend_a_ticket()
    {
        var armed = Arm();
        var ticket = armed.Tickets.Mint(Port);

        var decision = armed.Guard.Evaluate(Navigation("cross-site", Target(ticket), host: $"evil.example:{Port}"));

        Assert.AreEqual(LoopbackGuardVerdict.ForeignHost, decision.Verdict);
        Assert.IsTrue(armed.Tickets.TryConsume(ticket, Port));
    }

    [TestMethod]
    public void The_actions_that_run_on_this_machine_are_refused_to_another_sites_page()
    {
        // The reason reads are covered at all. Every one of these is a GET the relay
        // answers itself, on the user's machine: an <img> on any page — or on this
        // device's own origin, or another relay's, which are same-site — would start it.
        foreach (var target in new[]
                 {
                     "/tool/open?path=%2Fdata%2Fa.mkv", "/tool/open-file?path=%2Fdata%2Fa.mkv",
                     "/resource/1/play", "/resource/play/random", "/file/recycle-bin?path=x",
                     "/gui/url?url=https%3A%2F%2Fevil.example"
                 })
        foreach (var site in new[] {"same-site", "cross-site"})
        {
            Assert.AreEqual(LoopbackGuardVerdict.ForeignSite,
                Guard.Evaluate(Navigation(site, target, mode: "no-cors", dest: "image")).Verdict, $"{site} {target}");
        }
    }

    [TestMethod]
    public void Preflights_and_heads_from_another_site_are_refused()
    {
        Assert.AreEqual(LoopbackGuardVerdict.ForeignSite,
            Guard.Evaluate(Navigation("cross-site", "/resource/search", "OPTIONS", "cors", "empty")).Verdict);
        Assert.AreEqual(LoopbackGuardVerdict.ForeignSite,
            Guard.Evaluate(Navigation("same-site", "/file/play?fullname=x", "HEAD", "no-cors", "video")).Verdict);

        // From this relay's own page they are what they always were.
        Assert.AreEqual(LoopbackGuardVerdict.Allowed,
            Guard.Evaluate(Navigation("same-origin", "/resource/search", "OPTIONS", "cors", "empty")).Verdict);
        Assert.AreEqual(LoopbackGuardVerdict.Allowed,
            Guard.Evaluate(Navigation("same-origin", "/file/play?fullname=x", "HEAD", "no-cors", "video")).Verdict);
    }

    [TestMethod]
    public void A_fetch_site_no_browser_would_send_fails_closed()
    {
        foreach (var site in new[] {"bogus", "same-origin, cross-site", "cross-site,same-origin"})
        {
            Assert.AreEqual(LoopbackGuardVerdict.ForeignSite,
                Guard.Evaluate(Navigation(site, "/resource/search", mode: "cors", dest: "empty")).Verdict, site);
        }

        foreach (var site in new[] {"SAME-ORIGIN", " same-origin ", "None"})
        {
            Assert.AreEqual(LoopbackGuardVerdict.Allowed,
                Guard.Evaluate(Navigation(site, "/resource/search", mode: "cors", dest: "empty")).Verdict, site);
        }
    }

    [TestMethod]
    public void Same_origin_writes_still_answer_to_the_origin_rule()
    {
        Assert.AreEqual(LoopbackGuardVerdict.Allowed,
            Guard.Evaluate(Navigation("same-origin", "/resource/search", "POST", "cors", "empty", OurOrigin)).Verdict);
        Assert.AreEqual(LoopbackGuardVerdict.ForeignOrigin,
            Guard.Evaluate(Navigation("same-origin", "/resource/search", "POST", "cors", "empty",
                "https://evil.example")).Verdict);
        Assert.AreEqual(LoopbackGuardVerdict.ForeignOrigin,
            Guard.Evaluate(Navigation(null, "/resource/search", "DELETE", "", "", "https://evil.example")).Verdict);
    }

    [TestMethod]
    public void The_shells_own_navigation_carries_its_ticket_away()
    {
        // The tray points the window here itself, which the browser reports as a
        // navigation the user started. Admitted on that alone — but the ticket is still
        // spent and taken out, so it never reaches the server or the window's history.
        var armed = Arm();
        var ticket = armed.Tickets.Mint(Port);

        var decision = armed.Guard.Evaluate(Navigation("none", Target(ticket)));

        Assert.AreEqual(LoopbackGuardVerdict.DropTicket, decision.Verdict);
        Assert.AreEqual(TargetWithoutTicket, decision.ContinueTo);
        Assert.IsFalse(armed.Tickets.TryConsume(ticket, Port));
    }

    [TestMethod]
    public void A_guard_armed_with_no_tickets_admits_no_switch()
    {
        var guard = new LoopbackOriginGuard(Port);

        Assert.AreEqual(LoopbackGuardVerdict.ForeignSite,
            guard.Evaluate(Navigation("cross-site", Target(new string('a', 32)))).Verdict);

        // It still keeps a ticket-shaped parameter out of what it forwards.
        var decision = guard.Evaluate(Navigation("same-origin", Target(new string('a', 32))));
        Assert.AreEqual(LoopbackGuardVerdict.DropTicket, decision.Verdict);
        Assert.AreEqual(TargetWithoutTicket, decision.ContinueTo);
    }

    // ---- WebSocket handshakes ----

    public enum RequestKind
    {
        Get,
        Post,
        WebSocket,
        LabelledWebSocket,
        Http2WebSocket
    }

    /// <summary>
    /// A request of <paramref name="kind"/>, shaped as a browser shapes it. Chromium's
    /// handshake is an HTTP/1.1 GET with <c>Upgrade</c> and no fetch metadata; an engine that
    /// labels one says <c>Sec-Fetch-Mode: websocket</c>; HTTP/2's is an extended CONNECT.
    /// </summary>
    private static LoopbackGuardRequest Shaped(RequestKind kind, string? origin, string? site, string host,
        string target = "/hub/ui") => kind switch
    {
        RequestKind.Get => new("GET", host, origin, site, site == null ? null : "cors", site == null ? null : "empty", target),
        RequestKind.Post => new("POST", host, origin, site, site == null ? null : "cors", site == null ? null : "empty", target),
        RequestKind.WebSocket => new("GET", host, origin, site, null, null, target, WebSocket: true),
        RequestKind.LabelledWebSocket => new("GET", host, origin, site, "websocket", "websocket", target),
        RequestKind.Http2WebSocket => new("CONNECT", host, origin, site, null, null, target, WebSocket: true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Pages a request can come from: whether each is this relay's own.</summary>
    private static readonly (string Name, string? Origin, bool Ours)[] Pages =
    [
        ("own page", $"http://127.0.0.1:{Port}", true),
        ("own page on localhost", $"http://localhost:{Port}", true),
        ("another relay", $"http://127.0.0.1:{Port + 1}", false),
        ("another relay on localhost", $"http://localhost:{Port + 1}", false),
        ("this device's own window", "http://localhost:34567", false),
        ("website", "https://evil.example", false),
        ("opaque page", "null", false),
        ("no origin", null, false)
    ];

    /// <summary>
    /// What a browser honestly labels a request from <paramref name="origin"/> to
    /// <paramref name="host"/>: sites ignore ports, and <c>localhost</c> and <c>127.0.0.1</c>
    /// are two sites. An unnamed page is some other site's.
    /// </summary>
    private static string HonestSite(string? origin, string host)
    {
        if (origin == null || !Uri.TryCreate(origin, UriKind.Absolute, out var page) || page.Scheme != "http")
        {
            return "cross-site";
        }

        if (page.Authority.Equals(host, StringComparison.OrdinalIgnoreCase))
        {
            return "same-origin";
        }

        return page.Host.Equals(host[..host.LastIndexOf(':')], StringComparison.OrdinalIgnoreCase)
            ? "same-site"
            : "cross-site";
    }

    [TestMethod]
    public void The_websocket_matrix()
    {
        // The rule it pins: a handshake naming any page but this relay's own is refused as
        // ForeignOrigin, before fetch metadata is read — Chromium sends none on a handshake,
        // so the old rules let every page in any browser on this machine open the hub.
        // Everything that is not a handshake is judged exactly as before.
        foreach (var host in new[] {OurHost, $"localhost:{Port}"})
        foreach (var (name, origin, ours) in Pages)
        foreach (var kind in Enum.GetValues<RequestKind>())
        foreach (var withMetadata in new[] {false, true})
        {
            var site = withMetadata ? HonestSite(origin, host) : null;
            var label = $"{kind} to {host} from {name} ({origin ?? "no Origin"}), Sec-Fetch-Site: {site ?? "(absent)"}";
            var handshake = kind is RequestKind.WebSocket or RequestKind.LabelledWebSocket or RequestKind.Http2WebSocket;
            var safe = kind is RequestKind.Get or RequestKind.WebSocket or RequestKind.LabelledWebSocket;
            var foreignOrigin = origin != null && !ours;

            var expected = handshake && foreignOrigin
                ? LoopbackGuardVerdict.ForeignOrigin
                : site is "same-site" or "cross-site"
                    ? LoopbackGuardVerdict.ForeignSite
                    : !safe && foreignOrigin
                        ? LoopbackGuardVerdict.ForeignOrigin
                        : LoopbackGuardVerdict.Allowed;

            Assert.AreEqual(expected, Guard.Evaluate(Shaped(kind, origin, site, host)).Verdict, label);
        }
    }

    [TestMethod]
    public void Chromiums_handshake_from_another_page_is_refused()
    {
        // Exactly what a page on any website — or this device's own window, or another
        // relay — sends for new WebSocket("ws://127.0.0.1:<relay>/hub/ui").
        foreach (var origin in new[]
                 {
                     "https://evil.example", "http://localhost:34567", $"http://127.0.0.1:{Port + 1}", "null",
                     $"https://127.0.0.1:{Port}.evil.example", $"http://127.0.0.1:{Port}@evil.example"
                 })
        {
            Assert.AreEqual(LoopbackGuardVerdict.ForeignOrigin,
                Guard.Evaluate(new LoopbackGuardRequest("GET", OurHost, origin, Target: "/hub/ui?id=abc", WebSocket: true))
                    .Verdict, origin);
        }

        // Its own page's hub, and a native client's, are what they always were.
        Assert.AreEqual(LoopbackGuardVerdict.Allowed,
            Guard.Evaluate(new LoopbackGuardRequest("GET", OurHost, OurOrigin, Target: "/hub/ui?id=abc", WebSocket: true))
                .Verdict);
        Assert.AreEqual(LoopbackGuardVerdict.Allowed,
            Guard.Evaluate(new LoopbackGuardRequest("GET", OurHost, null, Target: "/hub/ui?id=abc", WebSocket: true))
                .Verdict);
    }

    [TestMethod]
    public void A_ticket_buys_a_foreign_handshake_nothing_and_is_not_spent()
    {
        var armed = Arm();
        var ticket = armed.Tickets.Mint(Port);

        foreach (var site in new string?[] {null, "same-site", "cross-site"})
        {
            var decision = armed.Guard.Evaluate(new LoopbackGuardRequest("GET", OurHost, "http://localhost:34567", site,
                site == null ? null : "websocket", site == null ? null : "websocket", Target(ticket), WebSocket: true));

            Assert.AreEqual(LoopbackGuardVerdict.ForeignOrigin, decision.Verdict, site ?? "(absent)");
            Assert.IsTrue(decision.CarriedTicket);
        }

        // Still there for the window it was minted for.
        Assert.AreEqual(LoopbackGuardVerdict.DropTicket, armed.Guard.Evaluate(Navigation("cross-site", Target(ticket))).Verdict);
    }

    [TestMethod]
    public void Its_own_page_is_recognised_in_every_form_the_host_check_accepts()
    {
        foreach (var origin in new[] {OurOrigin, $"http://localhost:{Port}", $"http://LOCALHOST:{Port}", $"http://[::1]:{Port}"})
        {
            Assert.IsTrue(Guard.IsOwnOrigin(origin), origin);
        }

        foreach (var origin in new[]
                 {
                     null, "", "null", $"http://127.0.0.1:{Port + 1}", "http://127.0.0.1", "https://evil.example",
                     $"http://evil.example:{Port}", $"http://127.0.0.1.evil.example:{Port}"
                 })
        {
            Assert.IsFalse(Guard.IsOwnOrigin(origin), origin ?? "(null)");
        }
    }
}
