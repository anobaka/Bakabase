using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Service.Components.DataSync;

namespace Bakabase.Service.Components.Federation;

/// <summary>
/// Manages sharing of a running headless instance from its own machine, where the Devices page is
/// unreachable: <c>docker exec &lt;container&gt; dotnet Bakabase.Service.dll federation invite</c>.
/// It only calls the instance's loopback-only management API, so it needs no extra permission model:
/// whoever can run a command inside the container already administers it.
/// </summary>
public static class FederationCli
{
    public const string Command = "federation";

    private const string Usage =
        """
        Usage: dotnet Bakabase.Service.dll federation <command> [--port <port>]
          status              Show sharing, devices and pending requests
          share on|off        Turn read-only sharing of this library on or off
          invite              Create a one-time code for another device
          approve <requestId> Approve a pending request (lets that device read this library)
          reject <requestId>  Reject a pending request
          revoke <grantId>    Stop a device from reading this library
          datasync <command>  Definitions sharing (run 'federation datasync' for its commands)
        The port defaults to API_LISTENING_PORTS, ASPNETCORE_HTTP_PORTS, then 8080.
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        var arguments = args.Skip(1).ToList();
        var port = TakeOption(arguments, "--port") ?? FirstPort("API_LISTENING_PORTS") ??
                   FirstPort("ASPNETCORE_HTTP_PORTS") ?? "8080";
        if (arguments.Count == 0 || !int.TryParse(port, out _))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        if (arguments[0] == "datasync") return await DataSyncCli.RunAsync(arguments.Skip(1).ToArray(), port);

        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
            { BaseAddress = new Uri($"http://127.0.0.1:{port}/federation/local/peers/") };
        try
        {
            var response = (arguments[0], arguments.Count) switch
            {
                ("status", 1) => await http.GetAsync(""),
                ("share", 2) when arguments[1] is "on" or "off" =>
                    await http.PutAsJsonAsync("sharing", new { enabled = arguments[1] == "on" }),
                ("invite", 1) => await http.PostAsync("invite", null),
                ("approve", 2) => await http.PostAsync($"requests/{Uri.EscapeDataString(arguments[1])}/approve", null),
                ("reject", 2) => await http.PostAsync($"requests/{Uri.EscapeDataString(arguments[1])}/reject", null),
                ("revoke", 2) => await http.DeleteAsync($"grants/{Uri.EscapeDataString(arguments[1])}"),
                _ => null
            };
            if (response == null)
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
            var body = await response.Content.ReadAsStringAsync();
            try
            {
                body = JsonSerializer.Serialize(JsonDocument.Parse(body).RootElement,
                    new JsonSerializerOptions { WriteIndented = true });
            }
            catch (JsonException) { }
            (response.IsSuccessStatusCode ? Console.Out : Console.Error).WriteLine(body);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException e)
        {
            Console.Error.WriteLine($"Bakabase is not reachable on 127.0.0.1:{port}: {e.Message}");
            return 1;
        }
    }

    private static string? TakeOption(System.Collections.Generic.List<string> arguments, string name)
    {
        var index = arguments.IndexOf(name);
        if (index < 0 || index + 1 >= arguments.Count) return null;
        var value = arguments[index + 1];
        arguments.RemoveRange(index, 2);
        return value;
    }

    private static string? FirstPort(string variable) =>
        Environment.GetEnvironmentVariable(variable)?.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).FirstOrDefault(x => int.TryParse(x, out _));
}
