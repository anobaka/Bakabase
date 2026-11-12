using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Service.Components.CloudDownloads.Rclone;
using Bakabase.TestKit.Implementations;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class RcloneConfigurationErrorsTests
{
    private string _root = null!;
    [TestInitialize]
    public void Setup() => _root = Path.Combine(Path.GetTempPath(), "rclone-config-errors-" + Guid.NewGuid().ToString("N"));
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [DataTestMethod]
    [DataRow("invalidRequest: Provided drive ID is malformed", "RCLONE_CONFIGURATION_REQUEST_INVALID")]
    [DataRow("accessDenied: https://private.invalid/?access_token=private-canary", "RCLONE_CONFIGURATION_ACCESS_DENIED")]
    [DataRow("InvalidAuthenticationToken: private-token", "RCLONE_CONFIGURATION_AUTHENTICATION_FAILED")]
    [DataRow("itemNotFound: private-drive-id", "RCLONE_CONFIGURATION_NOT_FOUND")]
    [DataRow("429 Too Many Requests", "RCLONE_CONFIGURATION_RATE_LIMITED")]
    [DataRow("503 Service Unavailable", "RCLONE_CONFIGURATION_SERVICE_UNAVAILABLE")]
    [DataRow("dial tcp: lookup private.invalid: no such host", "RCLONE_CONFIGURATION_NETWORK_FAILED")]
    [DataRow("x509: certificate signed by unknown authority", "RCLONE_CONFIGURATION_NETWORK_FAILED")]
    [DataRow("unknown upstream message private-token", "RCLONE_CONFIGURATION_ANSWER_REJECTED")]
    public void UpstreamTextBecomesOnlyAnAllowlistedCategory(string text, string code)
    {
        Assert.AreEqual(code, RcloneConfigurationErrors.Classify(text));
        Assert.IsFalse(RcloneConfigurationErrors.Classify(text).Contains("private", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task NonzeroNativeProcessExitIsClassifiedWithoutLeakingStderrIntoAnException()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix child process fixture.");
        Directory.CreateDirectory(_root);
        var executable = Path.Combine(_root, "fake-rclone");
        await File.WriteAllTextAsync(executable, "#!/bin/sh\nprintf '%s' 'accessDenied: https://private.invalid/?access_token=private-canary' >&2\nexit 7\n");
        File.SetUnixFileMode(executable, (UnixFileMode)0x1c0);
        var process = new RcloneConfigurationProcess(_ => Task.FromResult(executable), Proxy());
        var error = await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => process.ExecuteAsync(
            Path.Combine(_root, "stage"), "config/create", new {parameters = new { }}, default));
        Assert.AreEqual("RCLONE_CONFIGURATION_ACCESS_DENIED", error.Code);
        Assert.IsFalse(error.ToString().Contains("private-canary")); Assert.IsFalse(error.ToString().Contains("private.invalid"));
    }

    [TestMethod]
    public async Task OfficialNativeDriveRootErrorReturnsARecoverableStateAndSuccessfulRootReturnsABooleanConfirmation()
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_RCLONE_TEST_EXECUTABLE");
        if (string.IsNullOrEmpty(executable)) Assert.Inconclusive("Opt-in official binary fixture uses only an owned loopback Graph stub.");
        Directory.CreateDirectory(_root);
        var portProbe = new TcpListener(IPAddress.Loopback, 0); portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
        using var listener = new HttpListener(); var prefix = "http://127.0.0.1:" + port + "/";
        listener.Prefixes.Add(prefix); listener.Start();
        var responseNumber = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serving = Task.Run(async () =>
        {
            for (var index = 0; index < 2; index++)
            {
                var request = await listener.GetContextAsync().WaitAsync(timeout.Token);
                Assert.IsTrue(request.Request.RawUrl!.StartsWith("/v2.0/drives/fixture-drive/root", StringComparison.Ordinal));
                var failed = responseNumber++ == 0;
                request.Response.StatusCode = failed ? 400 : 200;
                request.Response.ContentType = "application/json";
                var json = failed ? """{"error":{"code":"invalidRequest","message":"fixture drive is unavailable"}}""" :
                    """{"id":"fixture-root","name":"root","folder":{"childCount":0},"parentReference":{"driveType":"personal","driveId":"fixture-drive"},"webUrl":"https://example.invalid/fixture"}""";
                var bytes = Encoding.UTF8.GetBytes(json); request.Response.ContentLength64 = bytes.Length;
                await request.Response.OutputStream.WriteAsync(bytes, timeout.Token); request.Response.Close();
            }
        }, timeout.Token);
        var stage = Path.Combine(_root, "native-stage"); Directory.CreateDirectory(stage);
        var token = JsonSerializer.Serialize(new {access_token = "fixture-only-access", refresh_token = "fixture-only-refresh", expiry = "2099-01-01T00:00:00Z"});
        await File.WriteAllTextAsync(Path.Combine(stage, "rclone.conf"), "[managed]\ntype = onedrive\ntoken = " + token + "\ntenant_url = " + prefix.TrimEnd('/') + "\n");
        var process = new RcloneConfigurationProcess(_ => Task.FromResult(executable!), Proxy());
        object Input(string state, string result) => new {opt = new {state, result}};
        var error = await process.ExecuteAsync(stage, "config/update", Input("driveid_final", "fixture-drive"), timeout.Token);
        Assert.AreEqual("choose_type", error.GetProperty("State").GetString());
        Assert.AreEqual(JsonValueKind.Null, error.GetProperty("Option").ValueKind);
        Assert.AreEqual("RCLONE_CONFIGURATION_REQUEST_INVALID", RcloneConfigurationErrors.Classify(error.GetProperty("Error").GetString()!));
        var choice = await process.ExecuteAsync(stage, "config/update", Input(error.GetProperty("State").GetString()!, ""), timeout.Token);
        Assert.AreEqual("config_type", choice.GetProperty("Option").GetProperty("Name").GetString());
        var confirm = await process.ExecuteAsync(stage, "config/update", Input("driveid_final", "fixture-drive"), timeout.Token);
        Assert.AreEqual("config_drive_ok", confirm.GetProperty("Option").GetProperty("Name").GetString());
        Assert.AreEqual(JsonValueKind.True, confirm.GetProperty("Option").GetProperty("Default").ValueKind);
        var finished = await process.ExecuteAsync(stage, "config/update", Input(confirm.GetProperty("State").GetString()!, "true"), timeout.Token);
        Assert.AreEqual("", finished.GetProperty("State").GetString());
        await serving.WaitAsync(timeout.Token);
        Assert.AreEqual(2, responseNumber);
    }

    private static BakabaseWebProxy Proxy() => new(new TestBOptionsManager<NetworkOptions>(new()));
}
