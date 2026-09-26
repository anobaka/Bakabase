using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Services;

namespace Bakabase.Service.Components.DataSync;

/// <summary>
/// <c>dotnet Bakabase.Service.dll federation datasync &lt;command&gt;</c>: manages definitions sharing of a running
/// headless instance from its own machine (spec §7.8). It only calls the instance's <c>/data-sync</c> API on loopback,
/// which lets loopback do everything, so it needs no permission model of its own. There is no inbox here (Q13):
/// <c>status</c> says what waits, and decisions are made on a desktop or through server switching.
/// </summary>
public static class DataSyncCli
{
    private const string Usage =
        """
        Usage: dotnet Bakabase.Service.dll federation datasync <command> [--port <port>]
          status                         Sharing and remote access, links (with what waits on each peer), readers, requests
          share on|off                   Let approved devices read this device's definitions (on also turns on remote access
                                         with pairing required when it is off)
          invite [--two-way]             One-time code for another device (also prints this device's addresses)
          requests                       Pending requests: direction, device name, the address it came from, the claim warning
                                         when it names a device this one knows at another address, a warning when a device
                                         already reads this one under its id (approving replaces that access), and what it
                                         asks for ("read this device's definitions" / "keep in step both ways")
          approve <requestId> [--no-receive-back]   Approve; two-way requests also sync back unless --no-receive-back
          reject <requestId>             Reject a request
          revoke <nodeId>                Stop a device from reading this device's definitions
          pause [<nodeId>] | resume [<nodeId>]      Pause or resume one link, or all
        The port defaults to API_LISTENING_PORTS, ASPNETCORE_HTTP_PORTS, then 8080.
        BAKABASE_DATASYNC_SHARING=true turns sharing on at every start (and remote access with pairing required when it
        is off), so turning sharing off lasts only until the next restart while the variable is set.
        """;

    public static async Task<int> RunAsync(string[] args, string port)
    {
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
            { BaseAddress = new Uri($"http://127.0.0.1:{port}/data-sync/") };
        try
        {
            return await RunAsync(args, http, Console.Out, Console.Error,
                Environment.GetEnvironmentVariable(DataSyncSharingAnnouncer.SharingVariable));
        }
        catch (HttpRequestException e)
        {
            await Console.Error.WriteLineAsync($"Bakabase is not reachable on 127.0.0.1:{port}: {e.Message}");
            return 1;
        }
    }

    /// <summary>The CLI over any client (its base address ends in <c>/data-sync/</c>) and any output.</summary>
    /// <param name="sharingVariable">The value of <c>BAKABASE_DATASYNC_SHARING</c> for this process.</param>
    internal static async Task<int> RunAsync(string[] args, HttpClient http, TextWriter output, TextWriter error,
        string? sharingVariable)
    {
        var arguments = args.ToList();
        var twoWay = arguments.Remove("--two-way");
        var noReceiveBack = arguments.Remove("--no-receive-back");
        var command = arguments.FirstOrDefault();
        if (command == null || (twoWay && command != "invite") || (noReceiveBack && command != "approve"))
        {
            await error.WriteLineAsync(Usage);
            return 2;
        }

        var cli = new Session(http, output, error);
        switch (command, arguments.Count)
        {
            case ("status", 1):
                return await cli.StatusAsync();
            case ("share", 2) when arguments[1] is "on" or "off":
            {
                var on = arguments[1] == "on";
                var result = await cli.SendAsync(HttpMethod.Put, "sharing",
                    new { enabled = on, enablePairedRemoteAccess = on });
                if (result is not { } data || cli.Refused(data)) return 1;
                await output.WriteLineAsync(on
                    ? "Definitions sharing is on. Only devices you approve can read this device's definitions."
                    : "Definitions sharing is off.");
                if (!on && bool.TryParse(sharingVariable, out var forced) && forced)
                {
                    await output.WriteLineAsync(
                        $"{DataSyncSharingAnnouncer.SharingVariable} is set, so sharing turns on again at the next start.");
                }

                return 0;
            }
            case ("invite", 1):
            {
                var result = await cli.SendAsync(HttpMethod.Post, "invitations", new { allowTwoWay = twoWay });
                if (result is not { } data || cli.Refused(data, "problem")) return 1;
                var invitation = data.GetProperty("invitation");
                await output.WriteLineAsync(
                    $"Code: {invitation.GetProperty("code").GetString()} (one use, expires {Time(invitation, "expiresAt")})");
                await output.WriteLineAsync(twoWay
                    ? "The device that uses it may also ask this device to keep in step with it."
                    : "The device that uses it can read this device's definitions.");
                var addresses = Strings(invitation, "addresses");
                await output.WriteLineAsync(addresses.Count == 0
                    ? "This device has no address other devices can reach."
                    : "Addresses of this device:");
                foreach (var address in addresses) await output.WriteLineAsync($"  {address}");
                return 0;
            }
            case ("requests", 1):
                return await cli.RequestsAsync(pendingOnly: true);
            case ("approve", 2):
            {
                var result = await cli.SendAsync(HttpMethod.Post, $"requests/{Uri.EscapeDataString(arguments[1])}/approve",
                    new { receiveBack = !noReceiveBack, kinds = (string[]?) null });
                if (result is not { } data || cli.Refused(data, "problem")) return 1;
                await output.WriteLineAsync("Approved.");
                if (data.TryGetProperty("createdLink", out var link) && link.ValueKind == JsonValueKind.Object)
                {
                    await output.WriteLineAsync(data.GetProperty("readBackGranted").GetBoolean()
                        ? $"This device now also receives from {link.GetProperty("peerName").GetString()}."
                        : $"Reading {link.GetProperty("peerName").GetString()} back did not work yet: " +
                          $"{Text(link, "lastErrorDetail") ?? Text(link, "lastErrorCode") ?? "unknown"}.");
                }

                return 0;
            }
            case ("reject", 2):
                return await cli.DoneAsync(HttpMethod.Post, $"requests/{Uri.EscapeDataString(arguments[1])}/reject",
                    null, "Rejected.");
            case ("revoke", 2):
                return await cli.DoneAsync(HttpMethod.Delete, $"readers/{Uri.EscapeDataString(arguments[1])}", null,
                    "That device can no longer read this device's definitions.");
            case ("pause" or "resume", 1):
                return await cli.DoneAsync(HttpMethod.Put, "paused", new { paused = command == "pause" },
                    command == "pause" ? "Every link is paused." : "Links resumed.");
            case ("pause" or "resume", 2):
            {
                var linkId = await cli.FindLinkAsync(arguments[1]);
                if (linkId == null)
                {
                    await error.WriteLineAsync($"No data sync link with {arguments[1]}.");
                    return 1;
                }

                var result = command == "pause"
                    ? await cli.SendAsync(HttpMethod.Post, $"links/{linkId}/pause", null)
                    : await cli.SendAsync(HttpMethod.Post, $"links/{linkId}/resume",
                        new { action = (int) DataSyncResumeAction.Resume });
                if (result is not { } data || cli.Refused(data, "problem")) return 1;
                await output.WriteLineAsync(command == "pause" ? "Paused." : "Resumed.");
                return 0;
            }
            default:
                await error.WriteLineAsync(Usage);
                return 2;
        }
    }

    /// <summary>One run's requests and printing.</summary>
    private sealed class Session(HttpClient http, TextWriter output, TextWriter error)
    {
        public async Task<int> StatusAsync()
        {
            if (await SendAsync(HttpMethod.Get, "overview", null) is not { } overview) return 1;
            if (await SendAsync(HttpMethod.Get, "links", null) is not { } links) return 1;
            if (await SendAsync(HttpMethod.Get, "readers", null) is not { } readers) return 1;

            await output.WriteLineAsync(
                $"This device: {Text(overview, "deviceName")} ({Text(overview, "nodeId")})" +
                (overview.GetProperty("isHeadless").GetBoolean() ? ", headless" : string.Empty));
            await output.WriteLineAsync(
                $"Definitions sharing: {(overview.GetProperty("sharingEnabled").GetBoolean() ? "on" : "off")}; " +
                $"remote access: {Name<RemoteAccessMode>(overview, "remoteAccessMode")}");
            await output.WriteLineAsync(overview.GetProperty("newDefinitionsStayLocal").GetBoolean()
                ? "New definitions stay on this device until shared."
                : "New definitions are shared automatically.");
            var status = overview.GetProperty("status");
            await output.WriteLineAsync(
                $"Status: {Name<DataSyncStatusLevel>(status, "level")}; " +
                $"{overview.GetProperty("openInboxItems").GetInt32()} change(s) need a decision here" +
                (overview.GetProperty("allPaused").GetBoolean() ? "; every link is paused" : string.Empty));
            if (overview.GetProperty("restorePending").GetBoolean())
            {
                await output.WriteLineAsync(
                    "This device's data looks restored: choose what wins in Data sync (a desktop managing this " +
                    "server can open it), then syncing continues.");
            }

            await output.WriteLineAsync($"Links ({links.GetArrayLength()}):");
            foreach (var link in links.EnumerateArray())
            {
                var line = $"  {Text(link, "peerName")} ({Text(link, "peerNodeId")}): " +
                           $"{Name<DataSyncLinkMode>(link, "mode")}, {Name<DataSyncLinkState>(link, "state")}";
                if (Name<DataSyncPauseReason>(link, "pausedReason") is { Length: > 0 } reason && reason != "?")
                    line += $" ({reason})";
                if (Text(link, "lastErrorCode") is { } code) line += $", error {code}";
                line += $", last synced {Time(link, "lastSyncedAt")}";
                await output.WriteLineAsync(line);

                var waits = new List<string>();
                if (link.GetProperty("openItems").GetInt32() is var open and > 0)
                    waits.Add($"{open} change(s) wait for a decision here");
                if (link.TryGetProperty("peerAttention", out var attention) &&
                    attention.ValueKind == JsonValueKind.Object)
                {
                    if (attention.GetProperty("openDecisions").GetInt32() is var there and > 0)
                        waits.Add($"{there} wait for a decision there");
                    if (attention.GetProperty("pausedLinks").GetInt32() is var paused and > 0)
                        waits.Add($"{paused} of its links paused");
                    if (attention.GetProperty("restorePending").GetBoolean())
                        waits.Add("it waits for a decision after a restore");
                }

                if (waits.Count > 0) await output.WriteLineAsync($"    {string.Join("; ", waits)}");
            }

            await output.WriteLineAsync($"Devices that read this one ({readers.GetArrayLength()}):");
            foreach (var reader in readers.EnumerateArray())
            {
                await output.WriteLineAsync(
                    $"  {Text(reader, "name")} ({Text(reader, "nodeId")}): {Text(reader, "mode") ?? "not read yet"}, " +
                    $"last read {Time(reader, "lastReadAt")}" +
                    (reader.GetProperty("upToDate").GetBoolean() ? ", up to date" : string.Empty));
            }

            return await RequestsAsync(pendingOnly: false);
        }

        public async Task<int> RequestsAsync(bool pendingOnly)
        {
            if (await SendAsync(HttpMethod.Get, "requests", null) is not { } requests) return 1;
            var shown = requests.EnumerateArray()
                .Where(r => !pendingOnly || Text(r, "status") is "awaitingApproval" or "pending")
                .ToList();
            await output.WriteLineAsync($"Requests ({shown.Count}):");
            foreach (var request in shown)
            {
                var incoming = request.GetProperty("direction").GetInt32() == (int) DataSyncRequestDirection.Incoming;
                var twoWay = request.GetProperty("intent").GetInt32() == (int) DataSyncRequestIntent.TwoWay;
                var asks = twoWay ? "keep in step both ways" : "read this device's definitions";
                await output.WriteLineAsync(incoming
                    ? $"  {Text(request, "requestId")}: from {Text(request, "nodeName")} at " +
                      $"{Text(request, "remoteAddress") ?? "an unknown address"}, asks to {asks} " +
                      $"[{Text(request, "status")}, expires {Time(request, "expiresAt")}]"
                    : $"  {Text(request, "requestId")}: to {Text(request, "nodeName")}, asking to " +
                      $"{(twoWay ? "keep in step both ways" : "read its definitions")} [{Text(request, "status")}]");
                if (incoming && request.GetProperty("claimsKnownDevice").GetBoolean())
                {
                    await output.WriteLineAsync(
                        $"    Warning: it says it comes from {Text(request, "nodeName")}, which this device knows at " +
                        $"{Text(request, "knownAddress")}. Only approve your own devices.");
                }

                // Said whatever the address says: a device known by a host name is never flagged as a claim above.
                if (incoming && Flag(request, "replacesExistingAccess"))
                {
                    await output.WriteLineAsync(
                        $"    Warning: a device known under the id {Text(request, "nodeId")} can already read this " +
                        "device's definitions. Approving replaces that access with this request's: if the request is " +
                        "not from that device, the device loses its access." +
                        (twoWay
                            ? " Unless approved with --no-receive-back, this device then also reads that id's " +
                              "definitions from the address this request offers, instead of from that device."
                            : string.Empty));
                }
            }

            return 0;
        }

        public async Task<int> DoneAsync(HttpMethod method, string path, object? body, string done)
        {
            if (await SendAsync(method, path, body) is not { } data || Refused(data)) return 1;
            await output.WriteLineAsync(done);
            return 0;
        }

        /// <summary>The <c>data</c> of the answer, or null after printing why there is none.</summary>
        public async Task<JsonElement?> SendAsync(HttpMethod method, string path, object? body)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body != null) request.Content = JsonContent.Create(body);
            else if (method != HttpMethod.Get && method != HttpMethod.Delete)
                request.Content = JsonContent.Create(new { }); // an empty body is refused as invalid
            using var response = await http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            JsonElement root;
            try
            {
                root = JsonDocument.Parse(text).RootElement.Clone();
            }
            catch (JsonException)
            {
                await error.WriteLineAsync($"Unexpected answer ({(int) response.StatusCode}): {text}");
                return null;
            }

            if (!response.IsSuccessStatusCode ||
                (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number &&
                 code.GetInt32() != 0))
            {
                await error.WriteLineAsync(
                    $"Failed ({(int) response.StatusCode}): {Text(root, "message") ?? text}");
                return null;
            }

            return root.TryGetProperty("data", out var data) ? data.Clone() : default(JsonElement);
        }

        /// <summary>
        /// Prints a data sync problem and says so: the answer itself (actions answering only a problem) or its
        /// <paramref name="member"/>.
        /// </summary>
        public bool Refused(JsonElement data, string? member = null)
        {
            var problem = member is null ? data : data.ValueKind == JsonValueKind.Object &&
                                                  data.TryGetProperty(member, out var p) ? p : default;
            if (problem.ValueKind != JsonValueKind.Object || !problem.TryGetProperty("code", out var code)) return false;
            var name = Enum.GetName(typeof(DataSyncProblemCode), code.GetInt32()) ?? code.ToString();
            error.WriteLine($"Refused: {name}{(Text(problem, "detail") is { } detail ? $" ({detail})" : string.Empty)}");
            return true;
        }

        public async Task<int?> FindLinkAsync(string peerNodeId)
        {
            if (await SendAsync(HttpMethod.Get, "links", null) is not { ValueKind: JsonValueKind.Array } links)
                return null;
            foreach (var link in links.EnumerateArray())
            {
                if (Text(link, "peerNodeId") == peerNodeId) return link.GetProperty("id").GetInt32();
            }

            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>True only where the field is there and true.</summary>
    private static bool Flag(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.True;

    /// <summary>Server times are UTC even without a zone (F70).</summary>
    private static string Time(JsonElement element, string name) =>
        Text(element, name) is { Length: > 0 } time ? $"{time} UTC" : "never";

    private static string Name<TEnum>(JsonElement element, string name) where TEnum : struct, Enum =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number
            ? Enum.GetName(typeof(TEnum), value.GetInt32()) ?? value.ToString()
            : "?";

    private static IReadOnlyList<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToList()
            : [];
}
