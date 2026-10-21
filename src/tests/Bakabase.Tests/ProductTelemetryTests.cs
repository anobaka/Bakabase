using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Telemetry;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components;
using Bakabase.Service.Services;
using Bakabase.Service.Services.Telemetry;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bakabase.Tests;

[TestClass]
public sealed class ProductTelemetryTests
{
    private const string DeploymentId = "408ab466-4986-4c59-8846-db2e0bdc3099";
    private const string ClientId = "bdafdd4c-e81a-4124-b4da-1bca7e7ef3f3";
    private const string SessionId = "5e31d861-12b5-47ec-8fb3-8889e6cd9910";

    [TestMethod]
    public async Task BackgroundAndClientEventsKeepSeparateIdentitiesAndOriginalTime()
    {
        using var fixture = new Fixture();
        var before = DateTimeOffset.UtcNow;
        fixture.Telemetry.Track("runtime_started");
        fixture.Context.HttpContext = ConsentingContext();
        fixture.Telemetry.Track("resource_search_completed", new Dictionary<string, object?>
        {
            ["result_count"] = 12, ["keyword"] = "private title", ["path"] = "/private/library",
            ["$current_url"] = "http://192.168.1.1/?token=secret", ["actor_type"] = "system"
        });
        var afterCapture = DateTimeOffset.UtcNow;
        fixture.Context.HttpContext = null;
        await fixture.Telemetry.FlushAsync(CancellationToken.None);

        using var payload = JsonDocument.Parse(fixture.Sender.Bodies.Single());
        var batch = payload.RootElement.GetProperty("batch").EnumerateArray().ToArray();
        Assert.AreEqual(2, batch.Length);
        var system = batch[0].GetProperty("properties");
        Assert.AreEqual("v2:deployment:" + DeploymentId, system.GetProperty("distinct_id").GetString());
        Assert.AreEqual("system", system.GetProperty("actor_type").GetString());
        Assert.IsFalse(system.GetProperty("$process_person_profile").GetBoolean());
        var client = batch[1].GetProperty("properties");
        Assert.AreEqual("v2:client:" + ClientId, client.GetProperty("distinct_id").GetString());
        Assert.AreEqual("client", client.GetProperty("actor_type").GetString());
        Assert.AreEqual(SessionId, client.GetProperty("$session_id").GetString());
        Assert.AreEqual("mobile_app", client.GetProperty("client_surface").GetString());
        Assert.AreEqual(12, client.GetProperty("result_count").GetInt32());
        Assert.AreEqual(2, client.GetProperty("schema_version").GetInt32());
        Assert.AreEqual(DeploymentId, client.GetProperty("deployment_id").GetString());
        Assert.IsFalse(fixture.Sender.Bodies[0].Contains("private"));
        Assert.IsFalse(fixture.Sender.Bodies[0].Contains("token=secret"));
        foreach (var item in batch)
        {
            var timestamp = item.GetProperty("timestamp").GetDateTimeOffset();
            Assert.IsTrue(timestamp >= before && timestamp <= afterCapture);
            Assert.IsTrue(Guid.TryParse(item.GetProperty("uuid").GetString(), out _));
        }
        Assert.AreNotEqual(batch[0].GetProperty("uuid").GetString(), batch[1].GetProperty("uuid").GetString());
    }

    [TestMethod]
    public async Task HttpWithoutExplicitValidConsentNeverBecomesSystemActivity()
    {
        using var fixture = new Fixture();
        fixture.Context.HttpContext = new DefaultHttpContext();
        fixture.Telemetry.Track("resource_search_completed");
        fixture.Context.HttpContext = ConsentingContext();
        fixture.Context.HttpContext.Request.Headers[TelemetryHttpContext.HeaderPrefix + "Consent"] = "0";
        fixture.Telemetry.Track("resource_search_completed", actor: new TelemetryActor(ClientId));
        fixture.Context.HttpContext.Request.Headers[TelemetryHttpContext.HeaderPrefix + "Consent"] = "1";
        fixture.Context.HttpContext.Request.Headers[TelemetryHttpContext.HeaderPrefix + "Client"] = "bad-client";
        fixture.Telemetry.Track("resource_search_completed");
        fixture.Context.HttpContext = null;
        fixture.Telemetry.Track("task_attempt_finished", actor: new TelemetryActor("private user"));
        await fixture.Telemetry.FlushAsync(CancellationToken.None);
        Assert.AreEqual(0, fixture.Sender.Bodies.Count);
    }

    [TestMethod]
    public async Task RevocationDropsQueuedEventsBeforeReenableAndDevelopmentNeverSends()
    {
        using var fixture = new Fixture();
        fixture.Telemetry.Track("runtime_started");
        fixture.Options.Value.EnableAnonymousDataTracking = false;
        await fixture.Telemetry.FlushAsync(CancellationToken.None);
        fixture.Options.Value.EnableAnonymousDataTracking = true;
        await fixture.Telemetry.FlushAsync(CancellationToken.None);
        Assert.AreEqual(0, fixture.Sender.Bodies.Count);
        fixture.Telemetry.Track("runtime_started");
        fixture.Telemetry.DiscardPending();
        await fixture.Telemetry.FlushAsync(CancellationToken.None);
        Assert.AreEqual(0, fixture.Sender.Bodies.Count);
        foreach (var name in new[] { "Development", "Test", "Testing", "CustomEnvironment" })
        {
            fixture.Environment.EnvironmentName = name;
            fixture.Telemetry.Track("runtime_started");
            await fixture.Telemetry.FlushAsync(CancellationToken.None);
            Assert.AreEqual(0, fixture.Sender.Bodies.Count, name);
        }
        fixture.Environment.EnvironmentName = "Staging";
        fixture.Telemetry.Track("runtime_started");
        await fixture.Telemetry.FlushAsync(CancellationToken.None);
        Assert.AreEqual(1, fixture.Sender.Bodies.Count);
    }

    [TestMethod]
    public async Task RevocationCancelsInflightDeliveryAndNeverReplaysItAfterReenable()
    {
        using var fixture = new Fixture();
        fixture.Sender.BlockUntilCancelled = true;
        fixture.Telemetry.Track("runtime_started");
        var flush = fixture.Telemetry.FlushAsync(CancellationToken.None);
        await fixture.Sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Options.Value.EnableAnonymousDataTracking = false;
        fixture.Telemetry.DiscardPending();
        await fixture.Sender.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await flush;
        fixture.Options.Value.EnableAnonymousDataTracking = true;
        fixture.Sender.BlockUntilCancelled = false;
        await fixture.Telemetry.FlushAsync(CancellationToken.None);
        Assert.AreEqual(1, fixture.Sender.Bodies.Count, "The revoked batch must not be replayed");
        fixture.Telemetry.Track("runtime_heartbeat");
        await fixture.Telemetry.FlushAsync(CancellationToken.None);
        Assert.AreEqual(2, fixture.Sender.Bodies.Count);
    }

    [TestMethod]
    public async Task QueueIsBoundedAndOfflineBatchIsNotRetried()
    {
        using var fixture = new Fixture(capacity: 2);
        for (var i = 0; i < 20; i++) fixture.Telemetry.Track("runtime_heartbeat");
        Assert.AreEqual(18L, fixture.Telemetry.DroppedCount);
        fixture.Sender.Status = HttpStatusCode.ServiceUnavailable;
        await fixture.Telemetry.FlushAsync(CancellationToken.None);
        await fixture.Telemetry.FlushAsync(CancellationToken.None);
        Assert.AreEqual(1, fixture.Sender.Bodies.Count);
        Assert.AreEqual(2L, fixture.Telemetry.FailedCount);
    }

    [TestMethod]
    public async Task IdentityUnavailableNeverSendsAndRevocationDuringCaptureNeverReplays()
    {
        using (var unavailable = new Fixture(deploymentId: string.Empty))
        {
            unavailable.Telemetry.Track("runtime_started");
            await unavailable.Telemetry.FlushAsync(CancellationToken.None);
            Assert.AreEqual(0, unavailable.Sender.Bodies.Count);
        }
        using var fixture = new Fixture();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = Task.Run(() => fixture.Telemetry.Track("runtime_started", new BlockingProperties(() =>
        {
            started.SetResult();
            release.Wait(TimeSpan.FromSeconds(5));
        })));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Options.Value.EnableAnonymousDataTracking = false;
        fixture.Telemetry.DiscardPending();
        fixture.Options.Value.EnableAnonymousDataTracking = true;
        release.Set();
        await capture;
        await fixture.Telemetry.FlushAsync(CancellationToken.None);
        Assert.AreEqual(0, fixture.Sender.Bodies.Count);
    }

    [TestMethod]
    public async Task DiagnosticsGateChecksConsentAtNetworkSendTime()
    {
        var allowed = false;
        var sender = new Sender();
        using var client = new HttpClient(new ConsentHttpMessageHandler(() => allowed, sender));
        using var ignored = await client.PostAsync("https://sentry.invalid/envelope", new StringContent("private"));
        Assert.AreEqual(0, sender.Bodies.Count);
        allowed = true;
        using var sent = await client.PostAsync("https://sentry.invalid/envelope", new StringContent("allowed"));
        Assert.AreEqual(1, sender.Bodies.Count);
        allowed = false;
        using var ignoredAgain = await client.PostAsync("https://sentry.invalid/envelope", new StringContent("private"));
        Assert.AreEqual(1, sender.Bodies.Count);
    }

    [TestMethod]
    public void UnknownEventsStringsAndUnboundedValuesAreRejected()
    {
        Assert.IsFalse(TelemetryEventPolicy.Allows("user-typed-event"));
        Assert.IsFalse(TelemetryEventPolicy.Allows("task_completed"));
        var values = TelemetryEventPolicy.Sanitize(new Dictionary<string, object?>
        {
            ["feature_id"] = "secret-file-name", ["error_category"] = "https://private/", ["duration_ms"] = double.NaN,
            ["resource_count"] = null, ["media_library_count"] = -1, ["result_count"] = 0,
            ["attempt_id"] = SessionId, ["snapshot_status"] = "partial"
        });
        Assert.AreEqual(4, values.Count);
        Assert.IsNull(values["resource_count"]);
        Assert.AreEqual(0, values["result_count"]);
        Assert.AreEqual(SessionId, values["attempt_id"]);
        Assert.IsNull(TelemetryHttpContext.Uuid(Guid.Empty.ToString("D")));
        Assert.AreEqual("unknown", TelemetryHttpContext.Surface("https://private/"));
        Assert.AreEqual("docker_server", TelemetryEnvironment.DetectDeployment(RuntimeMode.Docker, ServerKind.Headless, false));
        Assert.AreEqual("desktop_all_in_one", TelemetryEnvironment.DetectDeployment(RuntimeMode.Dev, ServerKind.Desktop, false));
        Assert.AreEqual("standalone_server", TelemetryEnvironment.DetectDeployment(RuntimeMode.Dev, ServerKind.Headless, false));
    }

    private static DefaultHttpContext ConsentingContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[TelemetryHttpContext.HeaderPrefix + "Consent"] = "1";
        context.Request.Headers[TelemetryHttpContext.HeaderPrefix + "Client"] = ClientId;
        context.Request.Headers[TelemetryHttpContext.HeaderPrefix + "Session"] = SessionId;
        context.Request.Headers[TelemetryHttpContext.HeaderPrefix + "Surface"] = "mobile_app";
        return context;
    }

    private sealed class Fixture : IDisposable
    {
        public MutableOptions Options { get; } = new();
        public HostEnvironment Environment { get; } = new();
        public HttpContextAccessor Context { get; } = new();
        public Sender Sender { get; } = new();
        public ProductTelemetry Telemetry { get; }
        private readonly HttpClient _client;

        public Fixture(int capacity = 10, string deploymentId = DeploymentId)
        {
            _client = new HttpClient(Sender);
            Telemetry = new ProductTelemetry(Options,
                Microsoft.Extensions.Options.Options.Create(new AnalyticsConfiguration
                {
                    PostHog = new AnalyticsConfiguration.PostHogOptions { ApiKey = "phc_test", ApiHost = "https://posthog.invalid", QueueCapacity = capacity }
                }), new TelemetryEnvironment(new DeviceIds(deploymentId), Environment, new ServerSelfDescription(() => ServerKind.Headless)),
                Context, new ClientFactory(_client), NullLogger<ProductTelemetry>.Instance);
        }
        public void Dispose() { Telemetry.Dispose(); _client.Dispose(); }
    }

    private sealed class DeviceIds(string id) : IDeviceIdService { public string GetOrCreate() => id; }
    private sealed class BlockingProperties(Action beforeRead) : IReadOnlyDictionary<string, object?>
    {
        public int Count => 1;
        public IEnumerable<string> Keys => ["duration_ms"];
        public IEnumerable<object?> Values => [1];
        public object? this[string key] => 1;
        public bool ContainsKey(string key) => key == "duration_ms";
        public bool TryGetValue(string key, out object? value) { value = 1; return ContainsKey(key); }
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            beforeRead();
            yield return new("duration_ms", 1);
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class MutableOptions : IBOptions<AppOptions> { public AppOptions Value { get; } = new(); }
    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Sender : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool BlockUntilCancelled { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            Started.TrySetResult();
            if (BlockUntilCancelled)
            {
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
            }
            return new HttpResponseMessage(Status) { Content = new StringContent("{}") };
        }
    }
    private sealed class HostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "TelemetryTests";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
