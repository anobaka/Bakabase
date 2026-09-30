using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Services;

namespace Bakabase.Service.Components.DataSync;

/// <summary>
/// <c>dotnet Bakabase.Service.dll federation datasync &lt;command&gt;</c>: manages definitions sharing of a running
/// headless instance from its own machine (spec §7.8). It only calls the instance's <c>/data-sync</c> API on loopback,
/// which lets loopback do everything, so it needs no permission model of its own. Like the library's CLI it prints the
/// answer as JSON (enums as numbers, times in UTC); only <c>requests</c> is prose, for the warnings a person must read
/// before approving. A refusal prints <c>Refused: {code}</c> and fails. There is no inbox here (Q13): decisions are
/// made on a desktop or through server switching.
/// </summary>
public static class DataSyncCli
{
    private const string Usage =
        """
        Usage: dotnet Bakabase.Service.dll federation datasync <command> [--port <port>]
          status                         Overview, links (with what waits on each peer), readers and requests, as JSON
          share on|off                   Let approved devices read this device's definitions (on also turns on remote access
                                         with pairing required when it is off)
          invite                         One-time code another device redeems to read this one (with this device's addresses)
          requests                       Pending requests: direction, device name, the address it came from, the claim warning
                                         when it names a device this one knows at another address, a warning when a device
                                         already reads this one under its id (approving replaces that access), and what it
                                         asks for ("read this device's definitions" / "keep in step both ways")
          approve <requestId> [--no-receive-back]   Approve; two-way requests also sync back unless --no-receive-back
          reject <requestId>             Reject a request
          revoke <nodeId>                Stop a device from reading this device's definitions
          pause [<nodeId>] | resume [<nodeId>]      Pause or resume one link, or all
        The port defaults to API_LISTENING_PORTS, ASPNETCORE_HTTP_PORTS, then 8080. Answers are JSON: enums as numbers,
        times in UTC. BAKABASE_DATASYNC_SHARING=true turns sharing on at every start (and remote access with pairing
        required when it is off), so turning sharing off lasts only until the next restart while the variable is set.
        """;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

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
        var noReceiveBack = arguments.Remove("--no-receive-back");
        var command = arguments.FirstOrDefault();
        var id = arguments.Count == 2 ? Uri.EscapeDataString(arguments[1]) : null;
        var cli = new Session(http, output, error);
        // Each command says where its answer carries a problem: the answer itself ("") or a member of it.
        switch (command, arguments.Count, noReceiveBack)
        {
            case ("status", 1, false):
            {
                var status = new JsonObject();
                foreach (var part in new[] { "overview", "links", "readers", "requests" })
                {
                    if (await cli.SendAsync(HttpMethod.Get, part, null) is not { } data) return 1;
                    status[part] = JsonNode.Parse(data.GetRawText());
                }

                await output.WriteLineAsync(status.ToJsonString(Indented));
                return 0;
            }
            case ("share", 2, false) when arguments[1] is "on" or "off":
            {
                var on = arguments[1] == "on";
                var exit = await cli.PrintAsync(HttpMethod.Put, "sharing",
                    new { enabled = on, enablePairedRemoteAccess = on }, "");
                if (exit == 0 && !on && bool.TryParse(sharingVariable, out var forced) && forced)
                {
                    await output.WriteLineAsync(
                        $"{DataSyncSharingAnnouncer.SharingVariable} is set, so sharing turns on again at the next start.");
                }

                return exit;
            }
            case ("invite", 1, false):
                return await cli.PrintAsync(HttpMethod.Post, "invitations", null, "problem");
            case ("requests", 1, false):
                return await cli.RequestsAsync();
            case ("approve", 2, _):
                return await cli.PrintAsync(HttpMethod.Post, $"requests/{id}/approve",
                    new { receiveBack = !noReceiveBack, kinds = (string[]?) null }, "problem");
            case ("reject", 2, false):
                return await cli.PrintAsync(HttpMethod.Post, $"requests/{id}/reject", null, "");
            case ("revoke", 2, false):
                return await cli.PrintAsync(HttpMethod.Delete, $"readers/{id}", null, "");
            case ("pause" or "resume", 1, false):
                return await cli.PrintAsync(HttpMethod.Put, "paused", new { paused = command == "pause" }, "");
            case ("pause" or "resume", 2, false):
            {
                if (await cli.SendAsync(HttpMethod.Get, "links", null) is not { } links) return 1;
                var link = links.EnumerateArray().FirstOrDefault(l => Text(l, "peerNodeId") == arguments[1]);
                if (link.ValueKind != JsonValueKind.Object)
                {
                    await error.WriteLineAsync($"No data sync link with {arguments[1]}.");
                    return 1;
                }

                var path = $"links/{link.GetProperty("id").GetInt32()}/{command}";
                return await cli.PrintAsync(HttpMethod.Post, path,
                    command == "pause" ? null : new { action = (int) DataSyncResumeAction.Resume }, "problem");
            }
            default:
                await error.WriteLineAsync(Usage);
                return 2;
        }
    }

    /// <summary>One run's requests and printing.</summary>
    private sealed class Session(HttpClient http, TextWriter output, TextWriter error)
    {
        /// <summary>
        /// Sends, then prints the answer as JSON — or, when the problem at <paramref name="problemAt"/> (the answer
        /// itself when empty) is there, <c>Refused: {code}</c>, and fails.
        /// </summary>
        public async Task<int> PrintAsync(HttpMethod method, string path, object? body, string problemAt)
        {
            if (await SendAsync(method, path, body) is not { } data) return 1;
            var problem = problemAt.Length == 0 ? data
                : data.ValueKind == JsonValueKind.Object && data.TryGetProperty(problemAt, out var p) ? p : default;
            if (problem.ValueKind == JsonValueKind.Object && problem.TryGetProperty("code", out var code))
            {
                var name = Enum.GetName(typeof(DataSyncProblemCode), code.GetInt32()) ?? code.ToString();
                await error.WriteLineAsync(
                    $"Refused: {name}{(Text(problem, "detail") is { } detail ? $" ({detail})" : string.Empty)}");
                return 1;
            }

            await output.WriteLineAsync(JsonSerializer.Serialize(data, Indented));
            return 0;
        }

        public async Task<int> RequestsAsync()
        {
            if (await SendAsync(HttpMethod.Get, "requests", null) is not { } requests) return 1;
            var shown = requests.EnumerateArray()
                .Where(r => Text(r, "status") is "awaitingApproval" or "pending").ToList();
            await output.WriteLineAsync($"Requests ({shown.Count}):");
            foreach (var request in shown)
            {
                var incoming = request.GetProperty("direction").GetInt32() == (int) DataSyncRequestDirection.Incoming;
                var twoWay = request.GetProperty("intent").GetInt32() == (int) DataSyncRequestIntent.TwoWay;
                var asks = twoWay ? "keep in step both ways" : "read this device's definitions";
                await output.WriteLineAsync(incoming
                    ? $"  {Text(request, "requestId")}: from {Text(request, "nodeName")} at " +
                      $"{Text(request, "remoteAddress") ?? "an unknown address"}, asks to {asks} " +
                      $"[{Text(request, "status")}, expires {Text(request, "expiresAt")} UTC]"
                    : $"  {Text(request, "requestId")}: to {Text(request, "nodeName")}, asking to " +
                      $"{(twoWay ? "keep in step both ways" : "read its definitions")} [{Text(request, "status")}]");
                if (incoming && request.GetProperty("claimsKnownDevice").GetBoolean())
                {
                    await output.WriteLineAsync(
                        $"    Warning: it says it comes from {Text(request, "nodeName")}, which this device knows at " +
                        $"{Text(request, "knownAddress")}. Only approve your own devices.");
                }

                // Said whatever the address says: a device known by a host name is never flagged as a claim above.
                if (incoming && request.TryGetProperty("replacesExistingAccess", out var replaces) &&
                    replaces.ValueKind == JsonValueKind.True)
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

        /// <summary>The <c>data</c> of the answer, or null after printing why there is none.</summary>
        public async Task<JsonElement?> SendAsync(HttpMethod method, string path, object? body)
        {
            using var request = new HttpRequestMessage(method, path);
            if (method != HttpMethod.Get && method != HttpMethod.Delete)
                request.Content = JsonContent.Create(body ?? new { }); // an empty body is refused as invalid
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

            if (!response.IsSuccessStatusCode || (root.TryGetProperty("code", out var code) &&
                                                  code.ValueKind == JsonValueKind.Number && code.GetInt32() != 0))
            {
                await error.WriteLineAsync($"Failed ({(int) response.StatusCode}): {Text(root, "message") ?? text}");
                return null;
            }

            return root.TryGetProperty("data", out var data) ? data.Clone() : JsonDocument.Parse("null").RootElement;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
