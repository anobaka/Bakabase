using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Bakabase.Service.Components.DataSync;

namespace Bakabase.Tests.DataSync.Api;

/// <summary>
/// The headless CLI (§7.8) over a stand-in for the instance's <c>/data-sync</c> API: what it asks for, what it prints,
/// and that a refusal fails the command.
/// </summary>
[TestClass]
public class DataSyncCliTests
{
    private sealed class StubApi : HttpMessageHandler
    {
        public ConcurrentQueue<(string Method, string Path, string? Body)> Requests { get; } = new();
        public Dictionary<string, string> Answers { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath["/data-sync/".Length..];
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Enqueue((request.Method.Method, path, body));
            var data = Answers.GetValueOrDefault($"{request.Method.Method} {path}", "null");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"code\":0,\"data\":{data}}}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static async Task<(int Exit, string Output, string Error, StubApi Api)> RunAsync(StubApi api,
        string? sharingVariable, params string[] args)
    {
        using var http = new HttpClient(api) { BaseAddress = new Uri("http://127.0.0.1:5000/data-sync/") };
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await DataSyncCli.RunAsync(args, http, output, error, sharingVariable);
        return (exit, output.ToString(), error.ToString(), api);
    }

    [TestMethod]
    public async Task Status_says_what_waits_on_each_peer_and_here()
    {
        var api = new StubApi();
        api.Answers["GET overview"] = """
            {"deviceName":"NAS","nodeId":"node-nas","isHeadless":true,"sharingEnabled":true,"remoteAccessMode":2,
             "newDefinitionsStayLocal":false,"allPaused":false,"openInboxItems":2,"restorePending":false,
             "status":{"level":3}}
            """;
        api.Answers["GET links"] = """
            [{"id":1,"peerNodeId":"node-pc","peerName":"PC-1","mode":2,"state":1,"lastSyncedAt":"2026-09-01 08:00:00.000",
              "openItems":2,"peerAttention":{"headless":false,"openDecisions":1,"pausedLinks":0,"restorePending":false}}]
            """;
        api.Answers["GET readers"] = """[{"nodeId":"node-pc","name":"PC-1","mode":"twoWay","lastReadAt":null,"upToDate":true}]""";
        api.Answers["GET requests"] = """
            [{"requestId":"r1","direction":1,"nodeId":"node-x","nodeName":"PC-1","intent":1,"status":"awaitingApproval",
              "expiresAt":"2026-09-01 08:10:00.000","remoteAddress":"192.168.1.66","claimsKnownDevice":true,
              "knownAddress":"http://192.168.1.10:5000"}]
            """;

        var (exit, output, _, _) = await RunAsync(api, null, "status");

        Assert.AreEqual(0, exit);
        StringAssert.Contains(output, "This device: NAS (node-nas), headless");
        StringAssert.Contains(output, "remote access: Unrestricted");
        StringAssert.Contains(output, "Status: NeedsYou; 2 change(s) need a decision here");
        StringAssert.Contains(output, "PC-1 (node-pc): TwoWay, Active, last synced 2026-09-01 08:00:00.000 UTC");
        StringAssert.Contains(output, "2 change(s) wait for a decision here; 1 wait for a decision there");
        StringAssert.Contains(output, "from PC-1 at 192.168.1.66, asks to read this device's definitions");
        StringAssert.Contains(output, "Warning: it says it comes from PC-1, which this device knows at http://192.168.1.10:5000");
    }

    /// <summary>
    /// A request under the id of a device that already reads this one says approving replaces that access, even with
    /// no claim warning (a device known by a host name is never flagged): approving cuts that device off.
    /// </summary>
    [TestMethod]
    public async Task Requests_say_when_approving_replaces_a_devices_access()
    {
        var api = new StubApi();
        api.Answers["GET requests"] = """
            [{"requestId":"r1","direction":1,"nodeId":"node-nas","nodeName":"NAS","intent":2,"status":"awaitingApproval",
              "expiresAt":"2026-09-01 08:10:00.000","remoteAddress":"192.168.1.66","claimsKnownDevice":false,
              "replacesExistingAccess":true},
             {"requestId":"r2","direction":1,"nodeId":"node-pc","nodeName":"PC","intent":1,"status":"awaitingApproval",
              "expiresAt":"2026-09-01 08:10:00.000","remoteAddress":"192.168.1.67","claimsKnownDevice":false,
              "replacesExistingAccess":true},
             {"requestId":"r3","direction":1,"nodeId":"node-new","nodeName":"New","intent":1,"status":"awaitingApproval",
              "expiresAt":"2026-09-01 08:10:00.000","remoteAddress":"192.168.1.68","claimsKnownDevice":false,
              "replacesExistingAccess":false}]
            """;

        var (exit, output, _, _) = await RunAsync(api, null, "requests");

        Assert.AreEqual(0, exit);
        var lines = output.Split(Environment.NewLine);
        string WarningAfter(string requestId) =>
            lines.SkipWhile(l => !l.StartsWith($"  {requestId}:")).Skip(1).TakeWhile(l => l.StartsWith("    "))
                .FirstOrDefault() ?? string.Empty;

        StringAssert.Contains(WarningAfter("r1"),
            "Warning: a device known under the id node-nas can already read this device's definitions. " +
            "Approving replaces that access with this request's");
        StringAssert.Contains(WarningAfter("r1"), "--no-receive-back", "a two-way request also re-points the read-back");
        StringAssert.Contains(WarningAfter("r2"), "the id node-pc can already read");
        Assert.IsFalse(WarningAfter("r2").Contains("--no-receive-back"), "a Follow request reads nothing back");
        Assert.AreEqual(string.Empty, WarningAfter("r3"));
        Assert.IsFalse(output.Contains("it says it comes from"), "no claim warning here");
    }

    [TestMethod]
    public async Task Sharing_off_says_the_variable_turns_it_on_again()
    {
        var (exit, output, _, api) = await RunAsync(new StubApi(), "true", "share", "off");

        Assert.AreEqual(0, exit);
        var request = api.Requests.Single();
        Assert.AreEqual(("PUT", "sharing"), (request.Method, request.Path));
        StringAssert.Contains(request.Body, "\"enabled\":false");
        StringAssert.Contains(output, "BAKABASE_DATASYNC_SHARING is set, so sharing turns on again at the next start.");
    }

    [TestMethod]
    public async Task Approve_without_receiving_back_says_so_and_a_refusal_fails()
    {
        var api = new StubApi();
        api.Answers["POST requests/r1/approve"] = """{"createdLink":null,"readBackGranted":false,"problem":null}""";
        var (exit, output, _, _) = await RunAsync(api, null, "approve", "r1", "--no-receive-back");
        Assert.AreEqual(0, exit);
        StringAssert.Contains(api.Requests.Single().Body, "\"receiveBack\":false");
        StringAssert.Contains(output, "Approved.");

        var refusing = new StubApi();
        refusing.Answers["POST invitations"] = """{"invitation":null,"problem":{"code":28,"detail":null}}""";
        var refused = await RunAsync(refusing, null, "invite", "--two-way");
        Assert.AreEqual(1, refused.Exit);
        StringAssert.Contains(refused.Error, "Refused: RemoteAccessOff");
        StringAssert.Contains(refusing.Requests.Single().Body, "\"allowTwoWay\":true");
    }

    [TestMethod]
    public async Task A_code_is_printed_with_the_addresses_to_use_it_at()
    {
        var api = new StubApi();
        api.Answers["POST invitations"] = """
            {"invitation":{"code":"48213705","expiresAt":"2026-09-01 08:10:00.000","addresses":["http://192.168.1.20:5000"],
             "allowTwoWay":false},"problem":null}
            """;
        var (exit, output, _, _) = await RunAsync(api, null, "invite");

        Assert.AreEqual(0, exit);
        StringAssert.Contains(output, "Code: 48213705 (one use, expires 2026-09-01 08:10:00.000 UTC)");
        StringAssert.Contains(output, "  http://192.168.1.20:5000");
    }

    [TestMethod]
    public async Task One_link_is_paused_by_its_peer_and_every_link_without_one()
    {
        var api = new StubApi();
        api.Answers["GET links"] = """[{"id":7,"peerNodeId":"node-pc"}]""";
        Assert.AreEqual(0, (await RunAsync(api, null, "pause", "node-pc")).Exit);
        Assert.IsTrue(api.Requests.Any(r => r is { Method: "POST", Path: "links/7/pause" }));

        Assert.AreEqual(1, (await RunAsync(api, null, "resume", "node-unknown")).Exit);

        var all = new StubApi();
        Assert.AreEqual(0, (await RunAsync(all, null, "resume")).Exit);
        StringAssert.Contains(all.Requests.Single().Body, "\"paused\":false");
    }

    [TestMethod]
    public async Task A_wrong_command_prints_the_usage()
    {
        var (exit, _, error, _) = await RunAsync(new StubApi(), null, "approve", "r1", "--two-way");
        Assert.AreEqual(2, exit);
        StringAssert.Contains(error, "BAKABASE_DATASYNC_SHARING=true turns sharing on at every start");
    }
}
