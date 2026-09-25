using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync.Services;

namespace Bakabase.Service.Components.DataSync;

/// <summary>
/// <c>dotnet Bakabase.Service.dll federation datasync &lt;command&gt;</c>: manages definitions sharing of a running
/// headless instance from its own machine (spec §7.8). It only calls the instance's <c>/data-sync</c> API on loopback,
/// which lets loopback do everything, so it needs no permission model of its own.
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
                                         when it names a device this one knows at another address, and what it asks for
                                         ("read this device's definitions" / "keep in step both ways")
          approve <requestId> [--no-receive-back]   Approve; two-way requests also sync back unless --no-receive-back
          reject <requestId>             Reject a request
          revoke <nodeId>                Stop a device from reading this device's definitions
          pause [<nodeId>] | resume [<nodeId>]      Pause or resume one link, or all
        The port defaults to API_LISTENING_PORTS, ASPNETCORE_HTTP_PORTS, then 8080.
        """;

    public static async Task<int> RunAsync(string[] args, string port)
    {
        var arguments = args.ToList();
        var twoWay = TakeFlag(arguments, "--two-way");
        var noReceiveBack = TakeFlag(arguments, "--no-receive-back");
        var command = arguments.FirstOrDefault();
        if (command == null || (twoWay && command != "invite") || (noReceiveBack && command != "approve"))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        using var http = new HttpClient(new SocketsHttpHandler {UseProxy = false})
            {BaseAddress = new Uri($"http://127.0.0.1:{port}/data-sync/")};
        try
        {
            switch (command, arguments.Count)
            {
                case ("status", 1):
                    foreach (var path in new[] {"overview", "links", "readers", "requests"})
                    {
                        if (await PrintAsync(await http.GetAsync(path)) != 0)
                        {
                            return 1;
                        }
                    }

                    return 0;
                case ("share", 2) when arguments[1] is "on" or "off":
                    var on = arguments[1] == "on";
                    return await PrintAsync(await http.PutAsJsonAsync("sharing",
                        new {enabled = on, enablePairedRemoteAccess = on}));
                case ("invite", 1):
                    return await PrintAsync(await http.PostAsJsonAsync("invitations", new {allowTwoWay = twoWay}));
                case ("requests", 1):
                    return await PrintAsync(await http.GetAsync("requests"));
                case ("approve", 2):
                    return await PrintAsync(await http.PostAsJsonAsync(
                        $"requests/{Uri.EscapeDataString(arguments[1])}/approve",
                        new {receiveBack = !noReceiveBack, kinds = (string[]?) null}));
                case ("reject", 2):
                    return await PrintAsync(
                        await http.PostAsync($"requests/{Uri.EscapeDataString(arguments[1])}/reject", null));
                case ("revoke", 2):
                    return await PrintAsync(await http.DeleteAsync($"readers/{Uri.EscapeDataString(arguments[1])}"));
                case ("pause" or "resume", 1):
                    return await PrintAsync(await http.PutAsJsonAsync("paused", new {paused = command == "pause"}));
                case ("pause" or "resume", 2):
                    var linkId = await FindLinkAsync(http, arguments[1]);
                    if (linkId == null)
                    {
                        Console.Error.WriteLine($"No data sync link with {arguments[1]}.");
                        return 1;
                    }

                    return await PrintAsync(command == "pause"
                        ? await http.PostAsync($"links/{linkId}/pause", null)
                        : await http.PostAsJsonAsync($"links/{linkId}/resume",
                            new {action = (int) DataSyncResumeAction.Resume}));
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (HttpRequestException e)
        {
            Console.Error.WriteLine($"Bakabase is not reachable on 127.0.0.1:{port}: {e.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Prints the answer, indented. Fails when the request failed, the envelope carries an error code, or the answer is
    /// a data sync problem: either the data itself (actions answering only a problem) or its <c>problem</c> member.
    /// </summary>
    private static async Task<int> PrintAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var failed = !response.IsSuccessStatusCode;
        try
        {
            var root = JsonDocument.Parse(body).RootElement;
            failed |= IsProblem(root);
            body = JsonSerializer.Serialize(root, new JsonSerializerOptions {WriteIndented = true});
        }
        catch (JsonException)
        {
        }

        (failed ? Console.Error : Console.Out).WriteLine(body);
        return failed ? 1 : 0;
    }

    private static bool IsProblem(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() != 0)
        {
            return true;
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return (data.TryGetProperty("problem", out var problem) && problem.ValueKind == JsonValueKind.Object) ||
               (data.TryGetProperty("code", out _) && data.TryGetProperty("detail", out _));
    }

    private static async Task<int?> FindLinkAsync(HttpClient http, string peerNodeId)
    {
        var response = await http.GetAsync("links");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!document.RootElement.TryGetProperty("data", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var link in links.EnumerateArray())
        {
            if (link.TryGetProperty("peerNodeId", out var nodeId) && nodeId.GetString() == peerNodeId &&
                link.TryGetProperty("id", out var id))
            {
                return id.GetInt32();
            }
        }

        return null;
    }

    private static bool TakeFlag(List<string> arguments, string name) => arguments.Remove(name);
}
