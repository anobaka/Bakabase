using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Service.Components.ServerData;
using Bakabase.Tests.RemoteAccess;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize] // Setup/progress Current and the container environment are process-wide.
public class ServerSetupHttpTests
{
    private string _root = null!;
    private DataDirectoryLock _dataLock = null!;
    private ImportProgressStore _monitoring = null!;
    private ServerSetupSession _setup = null!;
    private ServerSetupSession? _previousSetup;
    private ImportProgressStore? _previousMonitoring;
    private string? _previousContainer;

    [TestInitialize]
    public void Setup()
    {
        _root = ServerSetupSession.Canonical(Path.Combine(Path.GetTempPath(), "bakabase-setup-http-" + Guid.NewGuid().ToString("N")));
        var attempt = DataDirectoryLock.TryAcquire(_root);
        Assert.IsTrue(attempt.Acquired);
        _dataLock = attempt.Lock!;
        _monitoring = new ImportProgressStore(_root);
        _setup = new ServerSetupSession(_root, _root, true, _dataLock);
        _previousSetup = ServerSetupSession.Current;
        _previousMonitoring = ImportProgressStore.Current;
        _previousContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        ServerSetupSession.Current = _setup;
        ImportProgressStore.Current = _monitoring;
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        ServerSetupSession.Current = _previousSetup;
        ImportProgressStore.Current = _previousMonitoring;
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", _previousContainer);
        var selectedMonitoring = _setup.Monitoring;
        _setup.Dispose();
        selectedMonitoring?.Dispose();
        _monitoring.Dispose();
        _dataLock.Dispose();
        Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task FirstRunMonitorRemainsAuthorizedAcrossCoordinatorHandoff()
    {
        var result = _setup.Commit(new ServerSetupSession.SetupRequest { Operation = "initialize" }, _setup.Token);
        await using var server = await ImportProgressServer.StartAsync(_monitoring, ["http://127.0.0.1:0"]);
        using var client = new HttpClient { BaseAddress = new Uri(server.Addresses[0]) };
        client.DefaultRequestHeaders.Add(ImportProgressServer.TokenHeader, result.MonitorToken);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync(ImportProgressServer.StatusPath)).StatusCode);

        using var replacement = SetupProcessCoordinator.ReplaceMonitoring(_monitoring, _root);
        // Requests still select the submitted session while the passive store is published.
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync(ImportProgressServer.StatusPath)).StatusCode);
        _setup.Monitoring!.Dispose();
        _setup.Dispose();
        ServerSetupSession.Current = null;
        // This is the first request that previously fell back to an empty store.
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync(ImportProgressServer.StatusPath)).StatusCode);
        Assert.IsTrue(replacement.Authorize(result.MonitorToken));
    }

    [TestMethod]
    [DataRow("/setup/status", "GET")]
    [DataRow("/setup/validate", "POST")]
    [DataRow("/setup/apply", "POST")]
    [DataRow("/setup/browse", "POST")]
    [DataRow("/setup/directories", "GET")]
    [DataRow("/setup/draft", "GET")]
    [DataRow("/setup/draft", "POST")]
    [DataRow("/setup/draft/clear", "POST")]
    [DataRow("/setup/preflight", "GET")]
    [DataRow("/setup/preflight", "POST")]
    [DataRow("/setup/preflight/tree", "GET")]
    [DataRow("/setup/preflight/preview", "POST")]
    public async Task SetupEndpointsRejectMissingWrongAndMonitoringCapabilities(string path, string method)
    {
        _monitoring.EnsureQueued(new ServerAppDataImport.Journal());
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(_monitoring, [address]);
        using var client = Client(address);

        foreach (var token in new[] { null, new string('x', 64), _monitoring.Token })
        {
            using var response = await Send(client, path, method, token);
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var monitorHeader = await Send(client, path, method, _monitoring.Token,
            tokenHeader: ImportProgressServer.TokenHeader);
        Assert.AreEqual(HttpStatusCode.Unauthorized, monitorHeader.StatusCode);
        Assert.IsFalse(_setup.Submitted);
        Assert.IsNull(ServerAppDataImport.ReadPending(_root));
        AssertNoApplicationData();
    }

    [TestMethod]
    public async Task PublicSetupPageRevealsNoTokenOrDirectoryAndDoesNotStartTheApplication()
    {
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(_monitoring, [address]);
        using var client = Client(address);
        foreach (var path in new[] { "/", "/setup" })
        {
            using var page = await client.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
            Assert.IsTrue(page.Headers.CacheControl!.NoStore);
            Assert.AreEqual("no-referrer", page.Headers.GetValues("Referrer-Policy").Single());
            StringAssert.Contains(page.Headers.GetValues("Content-Security-Policy").Single(), "frame-ancestors 'none'");
            var html = await page.Content.ReadAsStringAsync();
            Assert.IsFalse(html.Contains(_setup.Token, StringComparison.Ordinal));
            Assert.IsFalse(html.Contains(_root, StringComparison.Ordinal));
        }
        using var application = await client.GetAsync("/app/info");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, application.StatusCode);
        AssertNoApplicationData();
    }

    [TestMethod]
    public async Task NativeLocalSessionRequiresPostAndSameOriginAndDockerCannotClaimIt()
    {
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(_monitoring, [address]);
        using var client = Client(address);
        using var get = await client.GetAsync("/setup/local-session");
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        using var localRequest = new HttpRequestMessage(HttpMethod.Post, "/setup/local-session");
        localRequest.Headers.Add("Origin", address);
        localRequest.Headers.Add("Sec-Fetch-Site", "same-origin");
        using var local = await client.SendAsync(localRequest);
        Assert.AreEqual(HttpStatusCode.OK, local.StatusCode);
        using var document = JsonDocument.Parse(await local.Content.ReadAsStringAsync());
        Assert.AreEqual(_setup.Token, document.RootElement.GetProperty("setupToken").GetString());

        using var foreignRequest = new HttpRequestMessage(HttpMethod.Post, "/setup/local-session");
        foreignRequest.Headers.Add("Origin", "https://untrusted.invalid");
        foreignRequest.Headers.Add("Sec-Fetch-Site", "cross-site");
        using var foreign = await client.SendAsync(foreignRequest);
        Assert.AreEqual(HttpStatusCode.Unauthorized, foreign.StatusCode);
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");
        using var container = await Send(client, "/setup/local-session", "POST");
        Assert.AreEqual(HttpStatusCode.Unauthorized, container.StatusCode);
        AssertNoApplicationData();
    }

    [TestMethod]
    public async Task AnAuthorizedSubmissionIsSingleUseAndOnlyReturnsAReadOnlyMonitoringCapability()
    {
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(_monitoring, [address]);
        using var client = Client(address);
        var token = _setup.Token;
        using var status = await Send(client, "/setup/status", "GET", token);
        Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
        using var statusJson = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.AreEqual(_root, statusJson.RootElement.GetProperty("currentPath").GetString());
        using var validated = await Send(client, "/setup/validate", "POST", token);
        using var validation = JsonDocument.Parse(await validated.Content.ReadAsStringAsync());
        Assert.IsTrue(validation.RootElement.GetProperty("valid").GetBoolean());
        Assert.IsFalse(_setup.Submitted);
        AssertNoApplicationData();

        using var submitted = await Send(client, "/setup/apply", "POST", token);
        Assert.AreEqual(HttpStatusCode.OK, submitted.StatusCode);
        using var result = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync());
        Assert.IsFalse(result.RootElement.GetProperty("requiresRestart").GetBoolean());
        var monitorToken = result.RootElement.GetProperty("monitorToken").GetString();
        Assert.AreNotEqual(token, monitorToken);
        var selection = await _setup.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(_root, selection.DataPath);
        Assert.IsTrue(_setup.Submitted);
        AssertNoApplicationData(); // The production caller starts the main host only after this handoff.

        using var repeated = await Send(client, "/setup/apply", "POST", token);
        Assert.AreEqual(HttpStatusCode.Unauthorized, repeated.StatusCode);
        using var cannotReclaim = await Send(client, "/setup/local-session", "POST");
        Assert.AreEqual(HttpStatusCode.Unauthorized, cannotReclaim.StatusCode);
        using var cannotMutate = await Send(client, "/setup/apply", "POST", monitorToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, cannotMutate.StatusCode);
        using var cannotBrowse = await Send(client, "/setup/directories", "GET", token);
        Assert.AreEqual(HttpStatusCode.Unauthorized, cannotBrowse.StatusCode);
        using var monitorCannotBrowse = await Send(client, "/setup/directories", "GET", monitorToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, monitorCannotBrowse.StatusCode);
        using var monitored = await Send(client, ImportProgressServer.StatusPath, "GET", monitorToken,
            tokenHeader: ImportProgressServer.TokenHeader);
        Assert.AreEqual(HttpStatusCode.OK, monitored.StatusCode);
        using var progress = JsonDocument.Parse(await monitored.Content.ReadAsStringAsync());
        Assert.AreEqual("starting", progress.RootElement.GetProperty("phase").GetString());
        using var setupCannotMonitor = await Send(client, ImportProgressServer.StatusPath, "GET", token,
            tokenHeader: ImportProgressServer.TokenHeader);
        Assert.AreEqual(HttpStatusCode.Unauthorized, setupCannotMonitor.StatusCode);
    }

    [TestMethod]
    public async Task AnInvalidSourceReturnsValidationFailureWithoutSubmittingOrQueuingAnImport()
    {
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(_monitoring, [address]);
        using var client = Client(address);
        var body = new { sourcePath = Path.Combine(_root, "missing-source") };
        using var response = await Send(client, "/setup/validate", "POST", _setup.Token, body);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var validation = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsFalse(validation.RootElement.GetProperty("valid").GetBoolean());
        using var rejected = await Send(client, "/setup/apply", "POST", _setup.Token, body);
        Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.IsFalse(_setup.Submitted);
        Assert.IsTrue(_setup.Authorize(_setup.Token));
        Assert.IsFalse(_setup.Completion.IsCompleted);
        Assert.IsNull(ServerAppDataImport.ReadPending(_root));
        AssertNoApplicationData();
    }

    [TestMethod]
    public async Task DirectoryPickerRequiresSetupAuthorizationAndDoesNotCommitItsSelection()
    {
        var calls = 0;
        var selected = Path.Combine(_root, "chosen-but-not-created");
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(_monitoring, [address], () =>
        {
            calls++;
            return Task.FromResult<string?>(selected);
        });
        using var client = Client(address);
        using var unauthorized = await Send(client, "/setup/browse", "POST");
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.AreEqual(0, calls);
        using var status = await Send(client, "/setup/status", "GET", _setup.Token);
        using var statusJson = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.IsTrue(statusJson.RootElement.GetProperty("canBrowse").GetBoolean());
        using var browsed = await Send(client, "/setup/browse", "POST", _setup.Token);
        Assert.AreEqual(HttpStatusCode.OK, browsed.StatusCode);
        using var path = JsonDocument.Parse(await browsed.Content.ReadAsStringAsync());
        Assert.AreEqual(selected, path.RootElement.GetProperty("path").GetString());
        Assert.AreEqual(1, calls);
        Assert.IsFalse(Directory.Exists(selected));
        Assert.IsFalse(_setup.Submitted);
        Assert.IsFalse(_setup.Completion.IsCompleted);
        AssertNoApplicationData();
    }

    [TestMethod]
    public async Task AServerWithoutANativePickerReturnsConflictAfterAuthorization()
    {
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(_monitoring, [address]);
        using var client = Client(address);
        using var response = await Send(client, "/setup/browse", "POST", _setup.Token);
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.IsFalse(_setup.Submitted);
        AssertNoApplicationData();
    }

    [TestMethod]
    public async Task AnExistingImportSessionCannotBeClaimedThroughTheFirstRunLocalEndpoint()
    {
        _setup.Dispose();
        _setup = ServerSetupSession.ForImport(_root, _monitoring);
        ServerSetupSession.Current = _setup;
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(_monitoring, [address]);
        using var client = Client(address);
        using var response = await Send(client, "/setup/local-session", "POST");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoApplicationData();
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task DirectoryBrowsingWorksWithoutANativePickerInFirstRunAndExistingContainerSessions(bool existing, bool container)
    {
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", container ? "true" : null);
        _setup.Dispose();
        var storage = new UserStoragePolicy(container, () =>
            $"1 0 0:1 / / rw - overlay overlay rw\n2 1 8:1 /data {_root} rw - ext4 /dev/test rw\n",
            () => _root);
        _setup = existing ? ServerSetupSession.ForImport(_root, _monitoring, storage: storage)
            : new ServerSetupSession(_root, _root, true, _dataLock, container, storage);
        ServerSetupSession.Current = _setup;
        var child = Path.Combine(_root, "existing library");
        Directory.CreateDirectory(child);
        Directory.CreateDirectory(Path.Combine(_root, ".hidden-directory"));
        File.WriteAllText(Path.Combine(_root, "private-file.txt"), "Never return these contents.");
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(_monitoring, [address]);
        using var client = Client(address);
        using var status = await Send(client, "/setup/status", "GET", _setup.Token);
        using var statusJson = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.IsTrue(statusJson.RootElement.GetProperty("canBrowseDirectories").GetBoolean());
        Assert.IsFalse(statusJson.RootElement.GetProperty("canBrowse").GetBoolean());
        using var response = await Send(client, "/setup/directories", "GET", _setup.Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        var content = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(content.Contains("private-file.txt", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("Never return", StringComparison.Ordinal));
        using var listing = JsonDocument.Parse(content);
        Assert.AreEqual(container ? "" : _root, listing.RootElement.GetProperty("currentPath").GetString());
        Assert.AreEqual(container ? null : Directory.GetParent(_root)!.FullName, listing.RootElement.GetProperty("parentPath").GetString());
        Assert.IsFalse(listing.RootElement.TryGetProperty("candidatePath", out _));
        CollectionAssert.AreEquivalent(container ? new[] { Path.GetFileName(_root) } : new[] { "existing library", ".hidden-directory" },
            listing.RootElement.GetProperty("directories").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray());
        Assert.IsTrue(listing.RootElement.GetProperty("roots").GetArrayLength() > 0);
        using var mounted = await Send(client, "/setup/directories?path=" + Uri.EscapeDataString(_root), "GET", _setup.Token);
        using var mountedListing = JsonDocument.Parse(await mounted.Content.ReadAsStringAsync());
        Assert.AreEqual(_root, mountedListing.RootElement.GetProperty("currentPath").GetString());
        Assert.AreEqual(container ? null : Directory.GetParent(_root)!.FullName,
            mountedListing.RootElement.GetProperty("parentPath").GetString());
        CollectionAssert.AreEquivalent(new[] { "existing library", ".hidden-directory" },
            mountedListing.RootElement.GetProperty("directories").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray());
        using var candidate = await Send(client, "/setup/directories?path=" + Uri.EscapeDataString(child) + "&newFolderName=new%20library",
            "GET", _setup.Token);
        Assert.AreEqual(HttpStatusCode.OK, candidate.StatusCode);
        using var selection = JsonDocument.Parse(await candidate.Content.ReadAsStringAsync());
        var expected = Path.Combine(child, "new library");
        Assert.AreEqual(expected, selection.RootElement.GetProperty("candidatePath").GetString());
        Assert.IsFalse(Directory.Exists(expected));
        Assert.IsFalse(_setup.Submitted);
        AssertNoApplicationData();
    }

    [TestMethod]
    [DataRow("path", "relative/path")]
    [DataRow("newFolderName", "..")]
    [DataRow("newFolderName", "")]
    [DataRow("newFolderName", "nested/child")]
    [DataRow("newFolderName", "nested\\child")]
    [DataRow("newFolderName", " trailing ")]
    public async Task InvalidBrowseInputsReturnBadRequestWithoutCreatingDirectories(string key, string value)
    {
        var before = Directory.GetFileSystemEntries(_root);
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(_monitoring, [address]);
        using var client = Client(address);
        using var response = await Send(client, "/setup/directories?" + key + "=" + Uri.EscapeDataString(value), "GET", _setup.Token);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        CollectionAssert.AreEquivalent(before, Directory.GetFileSystemEntries(_root));
        Assert.IsTrue(_setup.Authorize(_setup.Token));
    }

    [TestMethod]
    public async Task BrowseLengthLimitsMissingDirectoriesAndMethodsHaveExplicitResponses()
    {
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(_monitoring, [address]);
        using var client = Client(address);
        using var longPath = await Send(client, "/setup/directories?path=" + new string('a', 4097), "GET", _setup.Token);
        Assert.AreEqual(HttpStatusCode.BadRequest, longPath.StatusCode);
        using var longName = await Send(client, "/setup/directories?newFolderName=" + new string('a', 256), "GET", _setup.Token);
        Assert.AreEqual(HttpStatusCode.BadRequest, longName.StatusCode);
        using var missing = await Send(client, "/setup/directories?path=" + Uri.EscapeDataString(Path.Combine(_root, "missing")), "GET", _setup.Token);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        using var post = await Send(client, "/setup/directories", "POST", _setup.Token);
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        AssertNoApplicationData();
    }

    [TestMethod]
    public async Task BrowsingCanonicalizesDirectoryLinksAndBoundsLargeListings()
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        for (var i = 0; i <= ServerSetupDirectoryBrowser.MaxDirectories; i++)
            Directory.CreateDirectory(Path.Combine(target, "folder-" + i));
        var address = Address();
        await using var server = await ImportProgressServer.StartAsync(_monitoring, [address]);
        using var client = Client(address);
        using var large = await Send(client, "/setup/directories?path=" + Uri.EscapeDataString(target), "GET", _setup.Token);
        Assert.AreEqual(HttpStatusCode.OK, large.StatusCode);
        using var listing = JsonDocument.Parse(await large.Content.ReadAsStringAsync());
        Assert.IsTrue(listing.RootElement.GetProperty("truncated").GetBoolean());
        Assert.IsTrue(listing.RootElement.GetProperty("directories").GetArrayLength() <= ServerSetupDirectoryBrowser.MaxDirectories);
        if (!OperatingSystem.IsWindows())
        {
            var alias = Path.Combine(_root, "shortcut");
            Directory.CreateSymbolicLink(alias, target);
            using var linked = await Send(client, "/setup/directories?path=" + Uri.EscapeDataString(alias), "GET", _setup.Token);
            using var canonical = JsonDocument.Parse(await linked.Content.ReadAsStringAsync());
            Assert.AreEqual(target, canonical.RootElement.GetProperty("currentPath").GetString());
        }
        AssertNoApplicationData();
    }

    [TestMethod]
    public async Task FilesystemPermissionErrorsAreForbiddenRatherThanCapabilityErrors()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("This fixture requires Unix directory access modes.");
        var denied = Path.Combine(_root, "denied");
        Directory.CreateDirectory(denied);
        File.SetUnixFileMode(denied, 0);
        try
        {
            try
            {
                Directory.GetFileSystemEntries(denied);
                Assert.Inconclusive("This process can read mode-000 directories (for example, a root test runner).");
            }
            catch (UnauthorizedAccessException) { }
            var address = Address();
            await using var server = await ImportProgressServer.StartAsync(_monitoring, [address]);
            using var client = Client(address);
            using var response = await Send(client, "/setup/directories?path=" + Uri.EscapeDataString(denied), "GET", _setup.Token);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "permission");
            Assert.IsTrue(_setup.Authorize(_setup.Token));
        }
        finally { File.SetUnixFileMode(denied, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    private void AssertNoApplicationData()
    {
        Assert.IsFalse(File.Exists(Path.Combine(_root, "app.json")));
        Assert.IsFalse(Directory.EnumerateFiles(_root, "*.db", SearchOption.AllDirectories).Any());
    }

    private static string Address() => $"http://127.0.0.1:{LoopbackPortAllocator.Allocate(47700)}";
    private static HttpClient Client(string address) => new(new SocketsHttpHandler { UseProxy = false })
        { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, string method,
        string? token = null, object? body = null, string tokenHeader = ImportProgressServer.SetupTokenHeader)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (token != null) request.Headers.Add(tokenHeader, token);
        if (method == "POST") request.Content = JsonContent.Create(body ?? new { });
        return await client.SendAsync(request);
    }
}
