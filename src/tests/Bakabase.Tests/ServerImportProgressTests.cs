using System.Net;
using System.Text.Json;
using Bakabase.Service.Components.ServerData;
using Bakabase.Tests.RemoteAccess;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class ServerImportProgressTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-progress-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void AnIdleMonitorDoesNotCreateApplicationDataOrAuthorizeAnyone()
    {
        using var progress = new ImportProgressStore(_root);
        Assert.IsNull(progress.Read());
        Assert.IsNull(progress.Token);
        Assert.IsFalse(progress.Authorize(null));
        Assert.IsFalse(progress.Authorize(new string('A', 64)));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_root).Length);
    }

    [TestMethod]
    public void TokenAndOperationSurviveProcessRestartWithoutAppearingInPublicSnapshot()
    {
        var journal = new ServerAppDataImport.Journal();
        string token;
        using (var first = new ImportProgressStore(_root))
        {
            first.EnsureQueued(journal);
            token = first.Token!;
            Assert.AreEqual(64, token.Length);
            Assert.IsTrue(first.Authorize(token));
            Assert.IsFalse(first.Authorize(token[..63]));
            Assert.IsFalse(first.Authorize((token[0] == 'A' ? "B" : "A") + token[1..]));
            Assert.IsFalse(JsonSerializer.Serialize(first.Read()).Contains(token, StringComparison.Ordinal));
        }

        using var restored = new ImportProgressStore(_root);
        restored.EnsureQueued(journal);
        Assert.AreEqual(token, restored.Token);
        Assert.AreEqual(journal.Id, restored.Read()!.Id);
        Assert.AreEqual("queued", restored.Read()!.Phase);
        Assert.IsTrue(restored.Authorize(token));
        Assert.IsFalse(Directory.EnumerateFiles(_root, "*.db", SearchOption.AllDirectories).Any());
    }

    [TestMethod]
    public void RelocationStoreHandoffRetainsElapsedCopyTime()
    {
        var snapshot = new ImportProgressSnapshot
        { Id = Guid.NewGuid().ToString("N"), Operation = "relocate", Phase = "starting", ElapsedSeconds = 3725 };
        File.WriteAllText(Path.Combine(_root, ImportProgressStore.FileName), JsonSerializer.Serialize(
            new { token = new string('A', 64), progress = snapshot, applied = true }, ImportProgressStore.JsonOptions));
        using var restored = new ImportProgressStore(_root);
        restored.Starting();
        restored.Complete();
        Assert.IsTrue(restored.Read()!.ElapsedSeconds >= 3725);
        Assert.AreEqual("completed", restored.Read()!.Phase);
        Assert.AreEqual(new string('A', 64), restored.Token);
    }

    [TestMethod]
    public void ANewImportInvalidatesThePreviousMonitoringToken()
    {
        using var progress = new ImportProgressStore(_root);
        progress.EnsureQueued(new ServerAppDataImport.Journal());
        var oldToken = progress.Token!;
        progress.Cancel();
        Assert.AreEqual("cancelled", progress.Read()!.Phase);

        var next = new ServerAppDataImport.Journal();
        progress.EnsureQueued(next);
        Assert.AreNotEqual(oldToken, progress.Token);
        Assert.IsFalse(progress.Authorize(oldToken));
        Assert.AreEqual(next.Id, progress.Read()!.Id);
        Assert.AreEqual("queued", progress.Read()!.Phase);
    }

    [TestMethod]
    public void FullCopyAndInstalledDataDoNotDeclareTheApplicationReady()
    {
        using var progress = new ImportProgressStore(_root);
        progress.Begin(new ServerAppDataImport.Journal());
        progress.Report(new AppDataImportProgressUpdate("copying", 100, 100, 1, 1, "library.db"));
        Assert.AreEqual("copying", progress.Read()!.Phase);
        Assert.IsFalse(progress.Applied);
        progress.Starting();
        Assert.AreEqual("starting", progress.Read()!.Phase);
        Assert.IsTrue(progress.Applied);
        progress.Complete();
        Assert.AreEqual("completed", progress.Read()!.Phase);
        Assert.IsNull(progress.Read()!.CurrentFile);
        Assert.IsNull(progress.Read()!.RemainingSeconds);
    }

    [TestMethod]
    public async Task HeartbeatsEstimateCopyTimeButDoNotInventAnEtaForDatabaseVerification()
    {
        using var progress = new ImportProgressStore(_root);
        progress.Begin(new ServerAppDataImport.Journal());
        Assert.IsNull(progress.Read()!.RemainingSeconds);
        progress.Report(new AppDataImportProgressUpdate("copying", 25, 100, 0, 1, "library.db"));
        var lastActivity = progress.Read()!.LastActivityAtUtc;

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (progress.Read()!.BytesPerSecond <= 0 && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        var copying = progress.Read()!;
        Assert.IsTrue(copying.BytesPerSecond > 0);
        Assert.IsTrue(copying.RemainingSeconds > 0);
        Assert.IsTrue(copying.ElapsedSeconds > 0);
        Assert.AreEqual(25L, copying.CompletedBytes);
        Assert.AreEqual(lastActivity, copying.LastActivityAtUtc);
        Assert.IsTrue(copying.UpdatedAtUtc > copying.LastActivityAtUtc);

        progress.Report(new AppDataImportProgressUpdate("verifying", 100, 100, 1, 1));
        Assert.AreEqual(0, progress.Read()!.BytesPerSecond);
        Assert.IsNull(progress.Read()!.RemainingSeconds);
        Assert.IsTrue(progress.Read()!.LastActivityAtUtc > lastActivity);
    }

    [TestMethod]
    public void FailureRemainsReadableAfterRestartWithoutDeclaringCompletion()
    {
        string token;
        using (var progress = new ImportProgressStore(_root))
        {
            progress.Begin(new ServerAppDataImport.Journal());
            token = progress.Token!;
            progress.Report(new AppDataImportProgressUpdate("installing", 100, 100, 1, 1, "library.db"));
            progress.Fail("Disk is full. " + new string('x', 2500));
        }

        using var restored = new ImportProgressStore(_root);
        Assert.IsTrue(restored.Authorize(token));
        Assert.AreEqual("failed", restored.Read()!.Phase);
        Assert.AreEqual("installing", restored.Read()!.FailedPhase);
        Assert.AreEqual("library.db", restored.Read()!.CurrentFile);
        StringAssert.StartsWith(restored.Read()!.Error!, "Disk is full.");
        Assert.IsTrue(restored.Read()!.Error!.Length <= 2000);
        Assert.IsNull(restored.Read()!.RemainingSeconds);
        Assert.IsFalse(restored.Applied);
    }

    [TestMethod]
    public async Task MonitoringRemainsReadableWhileTheProgressWriterIsBlocked()
    {
        using var progress = Queued();
        var token = progress.Token;
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(progress, [address]);
        using var client = Client(address);
        client.Timeout = TimeSpan.FromSeconds(2);
        // Hold the writer's critical section as a stalled disk write would, without
        // depending on a specific filesystem or creating an actual hung I/O request.
        var gate = typeof(ImportProgressStore).GetField("_gate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(progress)!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var writer = Task.Run(() =>
        {
            lock (gate) { entered.Set(); release.Wait(); }
        });
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            using var response = await Send(client, ImportProgressServer.StatusPath, token);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var snapshot = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.AreEqual("queued", snapshot.RootElement.GetProperty("phase").GetString());
            using var unauthorized = await Send(client, ImportProgressServer.StatusPath, new string('x', 64));
            Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        }
        finally { release.Set(); await writer; }
    }

    [TestMethod]
    public void RetryingAnOperationClearsItsPreviousFailureDetails()
    {
        using var progress = new ImportProgressStore(_root);
        var journal = new ServerAppDataImport.Journal();
        progress.Begin(journal);
        progress.Report(new AppDataImportProgressUpdate("copying", 10, 100, 0, 1, "large.bin"));
        progress.Fail("copy failed");
        progress.Fail("startup failed");
        Assert.AreEqual("copying", progress.Read()!.FailedPhase);
        progress.Begin(journal);
        Assert.IsNull(progress.Read()!.FailedPhase);
        Assert.IsNull(progress.Read()!.Error);
        Assert.IsNull(progress.Read()!.CurrentFile);
        Assert.AreEqual("scanning", progress.Read()!.Phase);
    }

    [TestMethod]
    public void PersistedCapabilityIsPrivateToTheCurrentUnixUser()
    {
        if (OperatingSystem.IsWindows()) return;
        using var progress = new ImportProgressStore(_root);
        progress.EnsureQueued(new ServerAppDataImport.Journal());
        Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(Path.Combine(_root, ImportProgressStore.FileName)));
    }

    [TestMethod]
    [DataRow("not-json")]
    [DataRow("{\"token\":null,\"progress\":null}")]
    public void DamagedMonitoringMetadataDoesNotPreventRecoveringTheImportJournal(string content)
    {
        File.WriteAllText(Path.Combine(_root, ImportProgressStore.FileName), content);
        using var progress = new ImportProgressStore(_root);
        var journal = new ServerAppDataImport.Journal();
        progress.EnsureQueued(journal);
        Assert.AreEqual(journal.Id, progress.Read()!.Id);
        Assert.IsTrue(progress.Authorize(progress.Token));
    }

    [TestMethod]
    public async Task MonitoringRequiresItsOwnHeaderAndCannotAuthorizeApplicationRequests()
    {
        using var progress = Queued();
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(progress, [address]);
        using var client = Client(address);

        using var missing = await client.GetAsync(ImportProgressServer.StatusPath);
        Assert.AreEqual(HttpStatusCode.Unauthorized, missing.StatusCode);
        using var wrong = await Send(client, ImportProgressServer.StatusPath, new string('x', 64));
        Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.StatusCode);
        using var queryOnly = await client.GetAsync(ImportProgressServer.StatusPath + "?token=" + progress.Token);
        Assert.AreEqual(HttpStatusCode.Unauthorized, queryOnly.StatusCode);
        using var authorizationOnlyRequest = new HttpRequestMessage(HttpMethod.Get, ImportProgressServer.StatusPath);
        authorizationOnlyRequest.Headers.TryAddWithoutValidation("Authorization", "Bearer " + progress.Token);
        using var authorizationOnly = await client.SendAsync(authorizationOnlyRequest);
        Assert.AreEqual(HttpStatusCode.Unauthorized, authorizationOnly.StatusCode);

        using var signedRelayRequest = new HttpRequestMessage(HttpMethod.Get, ImportProgressServer.StatusPath);
        signedRelayRequest.Headers.Add(ImportProgressServer.TokenHeader, progress.Token);
        signedRelayRequest.Headers.TryAddWithoutValidation("Authorization", "Bakabase-Device unrelated-device-signature");
        using var authorized = await client.SendAsync(signedRelayRequest);
        Assert.AreEqual(HttpStatusCode.OK, authorized.StatusCode);
        using var snapshot = JsonDocument.Parse(await authorized.Content.ReadAsStringAsync());
        Assert.AreEqual(progress.Read()!.Id, snapshot.RootElement.GetProperty("id").GetString());
        Assert.AreEqual("queued", snapshot.RootElement.GetProperty("phase").GetString());
        Assert.IsFalse(snapshot.RootElement.TryGetProperty("data", out _));
        Assert.IsFalse(snapshot.RootElement.TryGetProperty("token", out _));
        Assert.IsFalse(snapshot.RootElement.TryGetProperty("monitorToken", out _));
        AssertPrivate(authorized);

        using var forbiddenMutation = await Send(client, ImportProgressServer.StatusPath, progress.Token, HttpMethod.Post);
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, forbiddenMutation.StatusCode);
        CollectionAssert.AreEqual(new[] { "GET" }, forbiddenMutation.Content.Headers.Allow.ToArray());
        using var ordinaryApi = await Send(client, "/app/data-path/import", progress.Token, HttpMethod.Post);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ordinaryApi.StatusCode);
        Assert.AreEqual(TimeSpan.FromSeconds(2), ordinaryApi.Headers.RetryAfter!.Delta);
        using var prefix = await Send(client, ImportProgressServer.StatusPath + "/other", progress.Token);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, prefix.StatusCode);
        using var preflightRequest = new HttpRequestMessage(HttpMethod.Options, ImportProgressServer.StatusPath);
        preflightRequest.Headers.Add("Origin", "https://untrusted.invalid");
        preflightRequest.Headers.Add("Access-Control-Request-Method", "GET");
        preflightRequest.Headers.Add("Access-Control-Request-Headers", ImportProgressServer.TokenHeader);
        using var preflight = await client.SendAsync(preflightRequest);
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, preflight.StatusCode);
        Assert.IsFalse(preflight.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.IsFalse(Directory.EnumerateFiles(_root, "*.db", SearchOption.AllDirectories).Any());
    }

    [TestMethod]
    public async Task ThePublicPageContainsNoOperationSecretsAndUsesAFreshCspNonce()
    {
        using var progress = Queued();
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(progress, [address]);
        using var client = Client(address);
        using var page = await client.GetAsync(ImportProgressServer.PagePath);
        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
        Assert.AreEqual("text/html", page.Content.Headers.ContentType!.MediaType);
        AssertPrivate(page);
        var html = await page.Content.ReadAsStringAsync();
        Assert.IsFalse(html.Contains(progress.Token!, StringComparison.Ordinal));
        Assert.IsFalse(html.Contains(_root, StringComparison.Ordinal));
        Assert.IsFalse(html.Contains("__NONCE__", StringComparison.Ordinal));
        var csp = page.Headers.GetValues("Content-Security-Policy").Single();
        StringAssert.Contains(csp, "script-src 'nonce-");
        StringAssert.Contains(csp, "connect-src 'self'");
        StringAssert.Contains(csp, "frame-ancestors 'none'");
        using var reloaded = await client.GetAsync(ImportProgressServer.PagePath);
        Assert.AreNotEqual(csp, reloaded.Headers.GetValues("Content-Security-Policy").Single());
        using var root = await client.GetAsync("/");
        Assert.AreEqual(HttpStatusCode.OK, root.StatusCode);
        Assert.AreEqual("text/html", root.Content.Headers.ContentType!.MediaType);
    }

    [TestMethod]
    public async Task AFailedImportKeepsItsReadOnlyMonitorAvailable()
    {
        using var progress = Queued();
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(progress, [address]);
        using var client = Client(address);
        progress.Fail("Cannot copy the source.");
        using var response = await Send(client, ImportProgressServer.StatusPath, progress.Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var snapshot = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("failed", snapshot.RootElement.GetProperty("phase").GetString());
        Assert.AreEqual("Cannot copy the source.", snapshot.RootElement.GetProperty("error").GetString());
        Assert.IsTrue(server.IsRunning);
        using var application = await client.GetAsync("/app/info");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, application.StatusCode);
        Assert.IsFalse(Directory.EnumerateFiles(_root, "*.db", SearchOption.AllDirectories).Any());
    }

    [TestMethod]
    public async Task HostedLifecycleHandsTheSamePortToTheMainHostWithoutLosingTheOperation()
    {
        using var progress = Queued();
        progress.Starting();
        var address = Address();
        await using var maintenance = await ImportProgressServer.StartAsync(progress, [address]);
        using var client = Client(address);
        using (var before = await Send(client, ImportProgressServer.StatusPath, progress.Token))
            Assert.AreEqual(HttpStatusCode.OK, before.StatusCode);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            { Args = [], ContentRootPath = _root, EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(address);
        var handedOff = false;
        builder.Services.AddSingleton<IHostedService>(new ImportProgressHandoff(async cancellation =>
        {
            await maintenance.StopAsync(cancellation);
            handedOff = true;
        }));
        await using var main = builder.Build();
        main.Run(async context =>
        {
            if (await ImportProgressServer.TryHandleAsync(context, progress)) return;
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
        });
        await main.StartAsync();
        try
        {
            Assert.IsTrue(handedOff);
            Assert.IsFalse(maintenance.IsRunning);
            using var starting = await Send(client, ImportProgressServer.StatusPath, progress.Token);
            Assert.AreEqual(HttpStatusCode.OK, starting.StatusCode);
            using var status = JsonDocument.Parse(await starting.Content.ReadAsStringAsync());
            Assert.AreEqual("starting", status.RootElement.GetProperty("phase").GetString());
            progress.Complete();
            using var complete = await Send(client, ImportProgressServer.StatusPath, progress.Token);
            using var completed = JsonDocument.Parse(await complete.Content.ReadAsStringAsync());
            Assert.AreEqual("completed", completed.RootElement.GetProperty("phase").GetString());
            using var missing = await client.GetAsync(ImportProgressServer.StatusPath);
            Assert.AreEqual(HttpStatusCode.Unauthorized, missing.StatusCode);
            using var unrelated = await Send(client, "/app/info", progress.Token);
            Assert.AreEqual(HttpStatusCode.Forbidden, unrelated.StatusCode);
            using var prefix = await Send(client, ImportProgressServer.StatusPath + "/other", progress.Token);
            Assert.AreEqual(HttpStatusCode.Forbidden, prefix.StatusCode);
        }
        finally { await main.StopAsync(); }
    }

    [TestMethod]
    public async Task MaintenanceListensOnEveryConfiguredPortAndReleasesThemOnStop()
    {
        using var progress = Queued();
        var first = Address();
        var second = $"http://127.0.0.1:{LoopbackPortAllocator.Allocate(new Uri(first).Port + 1)}";
        await using var maintenance = await ImportProgressServer.StartAsync(progress, [first, second]);
        using var firstClient = Client(first);
        using var secondClient = Client(second);
        using var firstResponse = await Send(firstClient, ImportProgressServer.StatusPath, progress.Token);
        using var secondResponse = await Send(secondClient, ImportProgressServer.StatusPath, progress.Token);
        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, secondResponse.StatusCode);
        await maintenance.StopAsync();
        await using var replacement = await ImportProgressServer.StartAsync(progress, [first, second]);
        using var reusedFirst = await Send(firstClient, ImportProgressServer.StatusPath, progress.Token);
        using var reusedSecond = await Send(secondClient, ImportProgressServer.StatusPath, progress.Token);
        Assert.AreEqual(HttpStatusCode.OK, reusedFirst.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, reusedSecond.StatusCode);
    }

    private ImportProgressStore Queued()
    {
        var progress = new ImportProgressStore(_root);
        progress.EnsureQueued(new ServerAppDataImport.Journal());
        return progress;
    }

    private static string Address() => $"http://127.0.0.1:{LoopbackPortAllocator.Allocate(47600)}";

    private static HttpClient Client(string address) => new(new SocketsHttpHandler { UseProxy = false })
        { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, string? token,
        HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        if (token != null) request.Headers.Add(ImportProgressServer.TokenHeader, token);
        return await client.SendAsync(request);
    }

    private static void AssertPrivate(HttpResponseMessage response)
    {
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        Assert.AreEqual("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.AreEqual("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.AreEqual("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.IsFalse(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
