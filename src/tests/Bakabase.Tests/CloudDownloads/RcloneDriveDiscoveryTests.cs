using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Service.Components.CloudDownloads.Rclone;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Implementations;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class RcloneDriveDiscoveryTests
{
    private const string Secret = "access-private-canary";
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static readonly JsonElement Profile = Json(new {type = "onedrive", token = JsonSerializer.Serialize(new {access_token = Secret, refresh_token = "refresh-private-canary"})});
    private static readonly RcloneConfigurationExampleViewModel[] Choices =
        [new("opaque", "opaque (business)"), new("opaque", "A friendly name (business)"), new("mine", "A friendly name (business)")];
    private static object Root => new {id = "root", name = "Root", folder = new {childCount = 2}, parentReference = new {driveType = "personal"}};

    [TestMethod]
    public async Task ExactIdsAreDeduplicatedAndOnlyMeDrivePlusReadableRootCanBecomePrimary()
    {
        var handler = new Http((request, _) => Task.FromResult(Reply(request.RequestUri!.AbsolutePath.EndsWith("/me/drive")
            ? new {id = "mine", name = "Personal files", driveType = "personal"} : Root)));
        var result = await new RcloneDriveDiscovery(handler).DescribeAsync("q", Profile, Choices, default);
        Assert.AreEqual(2, result.Candidates.Count);
        Assert.AreEqual("A friendly name (business)", result.Candidates[0].Name);
        Assert.AreEqual("unchecked", result.Candidates[0].AccessStatus); Assert.IsFalse(result.Candidates[0].IsPrimary);
        Assert.IsTrue(result.Candidates[1].IsPrimary); Assert.AreEqual("accessible", result.Candidates[1].AccessStatus);
        Assert.AreEqual("personal", result.Candidates[1].DriveType);
        Assert.AreEqual(2, handler.Paths.Count);
        Assert.AreEqual("/v1.0/drives/mine/root", handler.Paths[1]);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains(Secret));
    }

    [TestMethod]
    public async Task PrimaryNotInCurrentNativeChoicesIsNeverAddedOrGuessedFromTheName()
    {
        var handler = new Http((_, _) => Task.FromResult(Reply(new {id = "not-listed", name = "A friendly name (business)", driveType = "personal"})));
        var result = await new RcloneDriveDiscovery(handler).DescribeAsync("q", Profile, Choices, default);
        Assert.AreEqual("RCLONE_DRIVE_PRIMARY_NOT_LISTED", result.Problem);
        Assert.AreEqual(2, result.Candidates.Count); Assert.IsFalse(result.Candidates.Any(c => c.IsPrimary));
        Assert.AreEqual(1, handler.Paths.Count);
    }

    [DataTestMethod]
    [DataRow(400, "RCLONE_CONFIGURATION_REQUEST_INVALID")]
    [DataRow(401, "RCLONE_CONFIGURATION_AUTHENTICATION_FAILED")]
    [DataRow(403, "RCLONE_CONFIGURATION_ACCESS_DENIED")]
    [DataRow(404, "RCLONE_CONFIGURATION_NOT_FOUND")]
    [DataRow(429, "RCLONE_CONFIGURATION_RATE_LIMITED")]
    [DataRow(302, "RCLONE_CONFIGURATION_SERVICE_UNAVAILABLE")]
    public async Task FailedRootCannotRecommendADriveOrExposeErrorBodiesAndRedirects(int status, string code)
    {
        var handler = new Http((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/me/drive")
            ? Reply(new {id = "mine"}) : new HttpResponseMessage((HttpStatusCode)status)
            {Content = new StringContent("https://private.invalid?token=" + Secret), Headers = {Location = new("https://private.invalid")}}));
        var result = await new RcloneDriveDiscovery(handler).DescribeAsync("q", Profile, Choices, default);
        Assert.AreEqual(code, result.Problem); Assert.IsFalse(result.Candidates.Any(c => c.IsPrimary));
        Assert.AreEqual("unavailable", result.Candidates.Single(c => c.DriveId == "mine").AccessStatus);
        Assert.AreEqual(2, handler.Paths.Count);
        var json = JsonSerializer.Serialize(result); Assert.IsFalse(json.Contains(Secret)); Assert.IsFalse(json.Contains("private.invalid"));
    }

    [DataTestMethod]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("\"text\"")]
    [DataRow("invalid-json")]
    public async Task MalformedGraphShapesReturnOnlyTheStableMetadataCode(string body)
    {
        var handler = new Http((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content = new StringContent(body)}));
        var result = await new RcloneDriveDiscovery(handler).DescribeAsync("q", Profile, Choices, default);
        Assert.AreEqual("RCLONE_DRIVE_METADATA_INVALID", result.Problem);
    }

    [TestMethod]
    public async Task PreviewReadsOnlyTheBoundRootAndFirstPageAndNeverFollowsAContinuationOrReturnsUrls()
    {
        var handler = new Http((request, _) => Task.FromResult(Reply(request.RequestUri!.AbsolutePath.EndsWith("/children")
            ? new Dictionary<string, object> { ["value"] = new object[]
                {new {name = "folder", folder = new {}, size = 0, webUrl = "https://private.invalid"},
                 new {name = "file.txt", file = new {}, size = 23, downloadUrl = "https://private.invalid?token=" + Secret}},
                ["@odata.nextLink"] = "https://private.invalid?token=" + Secret } : Root)));
        var result = await new RcloneDriveDiscovery(handler).PreviewAsync("question", "drive!id", Profile, default);
        Assert.AreEqual("question", result.QuestionId); Assert.AreEqual("drive!id", result.DriveId);
        Assert.AreEqual(2, result.Items.Count); Assert.IsTrue(result.Items[0].IsDirectory);
        Assert.AreEqual(23L, result.Items[1].Size); Assert.IsTrue(result.HasMore);
        Assert.AreEqual(2, handler.Paths.Count); Assert.IsTrue(handler.Paths.All(p => Uri.UnescapeDataString(p).StartsWith("/v1.0/drives/drive!id/root")));
        var json = JsonSerializer.Serialize(result); Assert.IsFalse(json.Contains("private.invalid")); Assert.IsFalse(json.Contains(Secret));
    }

    [TestMethod]
    public void ProductionTransportDisablesCookiesAndRedirectsAndUsesToolThenSiteThenGlobalProxy()
    {
        var options = new NetworkOptions
        {
            Proxy = new() {Mode = NetworkOptions.ProxyMode.UseCustom, CustomProxyId = "global"},
            ThirdPartyProxies = new() {[(int)ThirdPartyId.OneDrive] = new() {Mode = NetworkOptions.ProxyMode.UseCustom, CustomProxyId = "site"}},
            DownloadToolProxies = new() {[DownloadToolProxyTargets.Rclone] = new() {Mode = NetworkOptions.ProxyMode.DoNotUse}},
            CustomProxies = [new() {Id = "global", Address = "http://127.0.0.1:18001"}, new() {Id = "site", Address = "http://127.0.0.1:18002"}]
        };
        using var handler = RcloneDriveDiscovery.CreateHandler(new BakabaseWebProxy(new TestBOptions<NetworkOptions>(options)));
        Assert.IsFalse(handler.AllowAutoRedirect); Assert.IsFalse(handler.UseCookies); Assert.IsTrue(handler.UseProxy);
        var graph = new Uri("https://graph.microsoft.com/v1.0/me/drive");
        Assert.IsNull(handler.Proxy!.GetProxy(graph));
        options.DownloadToolProxies!.Clear(); Assert.AreEqual(18002, handler.Proxy.GetProxy(graph)!.Port);
        options.ThirdPartyProxies!.Clear(); Assert.AreEqual(18001, handler.Proxy.GetProxy(graph)!.Port);
    }

    [DataTestMethod]
    [DataRow(nameof(RcloneAccountsController.GetDrives))]
    [DataRow(nameof(RcloneAccountsController.PreviewDrive))]
    public void MetadataEndpointsRetainBothOriginAndExpectedErrorGuards(string action)
    {
        var method = typeof(RcloneAccountsController).GetMethod(action)!;
        Assert.IsNotNull(method.GetCustomAttribute<RcloneBrowserOriginAttribute>());
        Assert.IsNotNull(method.GetCustomAttribute<RcloneAuthorizationErrorsAttribute>());
    }

    [TestMethod]
    public async Task SessionMetadataIsBoundCachedAndUpdatedByPreviewWithoutReplacingTheAccount()
    {
        using var f = new Fixture(); var session = await f.StartDriveQuestion();
        var choices = await f.Service.GetDrivesAsync(session.Id, session.Question!.Id, default);
        var reads = f.Process.ProfileReads;
        var cached = await f.Service.GetDrivesAsync(session.Id, session.Question.Id, default);
        Assert.AreEqual(reads, f.Process.ProfileReads); Assert.AreEqual(1, f.Discovery.Descriptions);
        Assert.AreEqual(2, cached.Candidates.Count);
        await f.Service.PreviewDriveAsync(session.Id, new() {QuestionId = session.Question.Id, DriveId = "opaque"}, default);
        var updated = await f.Service.GetDrivesAsync(session.Id, session.Question.Id, default);
        Assert.AreEqual("accessible", updated.Candidates.Single(c => c.DriveId == "opaque").AccessStatus);
        Assert.AreEqual(0, f.Profiles.Applies); Assert.IsFalse((await f.Accounts.ListAsync("oneDrive", default)).Single().IsConfigured);
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => f.Service.GetDrivesAsync(session.Id, "old-question", default));
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => f.Service.PreviewDriveAsync(session.Id,
            new() {QuestionId = session.Question.Id, DriveId = "not-a-native-candidate"}, default));
        Assert.AreEqual(reads + 1, f.Process.ProfileReads);
        Assert.AreEqual("awaitingInput", (await f.Service.GetAsync(session.Id, default)).Status);
    }

    [TestMethod]
    public async Task TransientDescriptionFailuresCanBeRetriedAndDoNotChangeTheNativeQuestionOrCredentials()
    {
        using var f = new Fixture(); var session = await f.StartDriveQuestion();
        f.Discovery.Problem = "RCLONE_CONFIGURATION_AUTHENTICATION_FAILED";
        Assert.IsNotNull((await f.Service.GetDrivesAsync(session.Id, session.Question!.Id, default)).Problem);
        f.Discovery.Problem = null;
        Assert.IsNull((await f.Service.GetDrivesAsync(session.Id, session.Question.Id, default)).Problem);
        Assert.AreEqual(2, f.Discovery.Descriptions); Assert.AreEqual(0, f.Profiles.Applies);
        Assert.AreEqual(session.Question.Id, (await f.Service.GetAsync(session.Id, default)).Question!.Id);
    }

    [TestMethod]
    public async Task PreviewDoesNotPoisonDiscoveryRetryAndTransientPreviewFailureCanRecoverPrimary()
    {
        using var f = new Fixture(); var session = await f.StartDriveQuestion();
        f.Discovery.Problem = "RCLONE_CONFIGURATION_NETWORK_FAILED";
        await f.Service.GetDrivesAsync(session.Id, session.Question!.Id, default);
        await f.Service.PreviewDriveAsync(session.Id, new() {QuestionId = session.Question.Id, DriveId = "opaque"}, default);
        f.Discovery.Problem = null;
        var retry = await f.Service.GetDrivesAsync(session.Id, session.Question.Id, default);
        Assert.AreEqual(2, f.Discovery.Descriptions); Assert.IsTrue(retry.Candidates.Single(c => c.DriveId == "mine").IsPrimary);
        Assert.AreEqual("accessible", retry.Candidates.Single(c => c.DriveId == "opaque").AccessStatus);
        f.Discovery.PreviewFailure = new("RCLONE_CONFIGURATION_NETWORK_FAILED");
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => f.Service.PreviewDriveAsync(session.Id,
            new() {QuestionId = session.Question.Id, DriveId = "mine"}, default));
        f.Discovery.PreviewFailure = null;
        var recovered = await f.Service.GetDrivesAsync(session.Id, session.Question.Id, default);
        Assert.AreEqual(3, f.Discovery.Descriptions);
        Assert.IsTrue(recovered.Candidates.Single(c => c.DriveId == "mine").IsPrimary);
        Assert.AreEqual("accessible", recovered.Candidates.Single(c => c.DriveId == "mine").AccessStatus);
        Assert.AreEqual(0, f.Profiles.Applies);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public async Task BlankDriveAnswersCannotSilentlyUseTheNativeDefaultEvenWhenRequiredIsFalse(string answer)
    {
        using var f = new Fixture(); var session = await f.StartDriveQuestion();
        Assert.IsFalse(session.Question!.Required); Assert.AreEqual("opaque", session.Question.DefaultValue);
        var error = await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => f.Service.AnswerAsync(session.Id,
            new() {QuestionId = session.Question.Id, Value = answer}, default));
        Assert.AreEqual("RCLONE_CONFIGURATION_ANSWER_INVALID", error.Code);
        Assert.AreEqual(0, f.Profiles.Applies);
        Assert.AreEqual(session.Question.Id, (await f.Service.GetAsync(session.Id, default)).Question!.Id);
    }

    [TestMethod]
    public async Task OtherNativeBranchesNeverProbeMeDriveButCanPreviewAnExistingCandidate()
    {
        using var f = new Fixture(); var session = await f.StartDriveQuestion("sharepoint");
        var result = await f.Service.GetDrivesAsync(session.Id, session.Question!.Id, default);
        Assert.AreEqual("RCLONE_DRIVE_PRIMARY_UNVERIFIED", result.Problem);
        Assert.IsTrue(result.Candidates.All(c => c.AccessStatus == "unchecked" && !c.IsPrimary));
        Assert.AreEqual(0, f.Discovery.Descriptions); Assert.AreEqual(0, f.Process.ProfileReads);
        await f.Service.PreviewDriveAsync(session.Id, new() {QuestionId = session.Question.Id, DriveId = "mine"}, default);
        Assert.AreEqual(1, f.Discovery.Previews);
    }

    [TestMethod]
    public async Task CancelInterruptsInflightPreviewAndNoLateResultCanPublishOrPopulateTheCache()
    {
        using var f = new Fixture(); var session = await f.StartDriveQuestion();
        f.Discovery.Block = true;
        var preview = f.Service.PreviewDriveAsync(session.Id, new() {QuestionId = session.Question!.Id, DriveId = "mine"}, default);
        await f.Discovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Service.CancelAsync(session.Id, default).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await preview);
        Assert.AreEqual("cancelled", (await f.Service.GetAsync(session.Id, default)).Status);
        Assert.AreEqual(0, f.Profiles.Applies);
        var saved = await File.ReadAllTextAsync(f.SessionPath(session.Id));
        Assert.IsFalse(saved.Contains(Secret)); Assert.IsFalse(saved.Contains("accessible"));
    }

    [TestMethod]
    public async Task QuestionChangeOrExpiryInvalidatesPreviouslyCachedDriveMetadata()
    {
        using var f = new Fixture(); var session = await f.StartDriveQuestion();
        await f.Service.GetDrivesAsync(session.Id, session.Question!.Id, default);
        f.Process.Results.Enqueue(Question("config_type", [new("onedrive", "OneDrive")]));
        await f.Service.AnswerAsync(session.Id, new() {QuestionId = session.Question.Id, Value = "mine"}, default);
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => f.Service.GetDrivesAsync(session.Id, session.Question.Id, default));
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(f.SessionPath(session.Id)))!;
        saved["expiresAt"] = DateTimeOffset.UtcNow.AddMinutes(-1);
        await File.WriteAllTextAsync(f.SessionPath(session.Id), saved.ToJsonString());
        var expired = await f.Service.GetAsync(session.Id, default); Assert.AreEqual("expired", expired.Status);
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => f.Service.PreviewDriveAsync(session.Id,
            new() {QuestionId = session.Question.Id, DriveId = "mine"}, default));
        Assert.AreEqual(0, f.Profiles.Applies);
    }

    private static JsonElement Question(string name, IReadOnlyList<RcloneConfigurationExampleViewModel> choices) => Json(new
    {
        State = name == "config_type" ? "choose_type_done" : "driveid_final",
        Option = new {Name = name, Help = "Choose", Type = "string", Required = name == "config_type", Exclusive = name == "config_type", Default = "opaque",
            Examples = choices.Select(c => new {c.Value, c.Help})}
    });
    private static HttpResponseMessage Reply(object value) => new(HttpStatusCode.OK) {Content = new StringContent(JsonSerializer.Serialize(value))};
    private sealed class Http(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.AreEqual("graph.microsoft.com", request.RequestUri!.Host); Assert.AreEqual("https", request.RequestUri.Scheme);
            Assert.AreEqual(HttpMethod.Get, request.Method); Assert.AreEqual(Secret, request.Headers.Authorization!.Parameter);
            Assert.IsFalse(request.RequestUri.AbsoluteUri.Contains(Secret)); Paths.Add(request.RequestUri.AbsolutePath);
            return send(request, ct);
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rclone-drives-" + Guid.NewGuid().ToString("N"));
        public Profiles Profiles { get; } = new();
        public Native Process { get; } = new();
        public Discovery Discovery { get; } = new();
        public RcloneAccountStore Accounts { get; }
        public RcloneAuthorizationService Service { get; }
        public Fixture()
        {
            Accounts = new(Path.Combine(_root, "accounts"), Profiles);
            Service = new(Path.Combine(_root, "authorization"), Accounts, Process, drives: Discovery);
        }
        public string SessionPath(string id) => Path.Combine(_root, "authorization", id, "session.json");
        public async Task<RcloneAuthorizationSessionViewModel> StartDriveQuestion(string type = "onedrive")
        {
            var account = await Accounts.SaveAsync(new() {ProviderId = "oneDrive", Name = "untouched"}, default);
            Process.Results.Enqueue(Question("config_type", [new("onedrive", "OneDrive"), new("sharepoint", "SharePoint")]));
            var started = await Service.StartAsync(account.Id, default);
            Process.Results.Enqueue(Question("config_driveid", Choices));
            return await Service.AnswerAsync(started.Id, new() {QuestionId = started.Question!.Id, Value = type}, default);
        }
        public void Dispose() {if (Directory.Exists(_root)) Directory.Delete(_root, true);}
    }
    private sealed class Native : IRcloneConfigurationProcess
    {
        public Queue<JsonElement> Results { get; } = new();
        public int ProfileReads { get; private set; }
        public Task<JsonElement> ExecuteAsync(string directory, string method, object input, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (method == "config/get") {ProfileReads++; return Task.FromResult(Profile);}
            return Task.FromResult(Results.Dequeue());
        }
    }
    private sealed class Discovery : IRcloneDriveDiscovery
    {
        public int Descriptions { get; private set; }
        public int Previews { get; private set; }
        public string? Problem { get; set; }
        public bool Block { get; set; }
        public RcloneRemoteException? PreviewFailure { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RcloneDriveChoicesViewModel> DescribeAsync(string questionId, JsonElement profile, IReadOnlyList<RcloneConfigurationExampleViewModel> examples, CancellationToken ct)
        {
            Descriptions++;
            return Task.FromResult(new RcloneDriveChoicesViewModel {QuestionId = questionId, Candidates = RcloneDriveDiscovery.Candidates(examples)
                .Select(c => c.DriveId == "mine" && Problem == null ? c with {IsPrimary = true, AccessStatus = "accessible"} : c).ToList(), Problem = Problem});
        }
        public async Task<RcloneDrivePreviewViewModel> PreviewAsync(string questionId, string driveId, JsonElement profile, CancellationToken ct)
        {
            Previews++; Started.TrySetResult();
            if (PreviewFailure != null) throw PreviewFailure;
            if (Block) await Task.Delay(Timeout.Infinite, ct);
            return new() {QuestionId = questionId, DriveId = driveId, Items = [new("item.txt", false, 1)]};
        }
    }
    private sealed class Profiles : IRcloneAccountProfiles
    {
        public int Applies { get; private set; }
        public Task<bool> HasAsync(RcloneProfileRef profile, CancellationToken ct) => Task.FromResult(false);
        public Task<string?> VersionAsync(RcloneProfileRef profile, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task RemoveAsync(RcloneProfileRef profile, CancellationToken ct) => Task.CompletedTask;
        public Task ConfigurePikPakAsync(string id, string user, string pass, CancellationToken ct) => Task.CompletedTask;
        public Task ConfigurePixeldrainAsync(string id, string root, string? key, CancellationToken ct) => Task.CompletedTask;
        public Task ConfigureOneDriveAsync(string id, RcloneOneDriveConfiguration config, CancellationToken ct) {Applies++; return Task.CompletedTask;}
        public Task ValidateAsync(RcloneProfileRef profile, CancellationToken ct) => Task.CompletedTask;
    }
}
