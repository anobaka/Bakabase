using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Remoting.Abstractions;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Forwarding;
using Bakabase.Remoting.Components.UserMachine;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// The small handlers: a refusal, a null, a loopback link and an artifact.
/// </summary>
/// <remarks>
/// Each of them answers a question the server cannot, and each gets it wrong in a
/// different way if written carelessly — by offering configuration for something no
/// configuration fixes, by reporting an unreachable server as a missing mapping, or by
/// building a link that points at the server the browser cannot sign for.
/// </remarks>
[TestClass]
public class SimpleUserMachineHandlerTests
{
    private string _root = null!;
    private ActiveConnection _connection = null!;
    private RecordingShell _shell = null!;
    private StubUpstreamApi _upstream = null!;

    private sealed class TempDirectory(string path) : IClientDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    private sealed class RecordingShell : IShellOpener
    {
        public readonly List<(string Path, bool InParent)> Revealed = [];
        public readonly List<string> Launched = [];
        public Exception? LaunchThrows;

        public void Reveal(string path, bool inParentDirectory) => Revealed.Add((path, inParentDirectory));

        public void Launch(string target)
        {
            if (LaunchThrows != null)
            {
                throw LaunchThrows;
            }

            Launched.Add(target);
        }

        public void LaunchProcess(string executable, string arguments, bool useShellExecute) =>
            throw new NotSupportedException();

        public void LaunchProgram(string path, string? workingDirectory) =>
            throw new NotSupportedException();
    }

    private sealed class StubLoopback(string baseAddress) : ILoopbackAddressProvider
    {
        public string BuildRawFileUrl(string serverPath) =>
            $"{baseAddress}/file/raw?fullname={Uri.EscapeDataString(serverPath)}";

        public string BuildUrl(string pathAndQuery) => $"{baseAddress}/{pathAndQuery.TrimStart('/')}";
    }

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-simple-handler-tests", Guid.NewGuid().ToString("N"));
        _connection = new ActiveConnection(new ClientConnectionStore(new TempDirectory(_root)));
        _shell = new RecordingShell();
        _upstream = new StubUpstreamApi();
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (Exception e) when (e is IOException or DirectoryNotFoundException)
        {
        }
    }

    private async Task<string> WithLibraryAt(string serverPath)
    {
        var local = Path.Combine(_root, "library");
        Directory.CreateDirectory(local);

        await _connection.SaveAsync("server-1", "Desk", "http://192.168.1.5:34567",
            new ClientCredentials("d1", "k1"), DateTime.UtcNow);

        await _connection.SetPathMappingsAsync("server-1",
            [new ClientPathMapping {ServerPath = serverPath, LocalPath = local}]);

        return local;
    }

    private static DefaultHttpContext Request()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static (int Status, JsonElement Body, string? ClientHeader) Read(HttpContext context)
    {
        context.Response.Body.Position = 0;
        var text = new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEnd();

        return (context.Response.StatusCode,
            JsonDocument.Parse(text).RootElement,
            context.Response.Headers.TryGetValue("X-Bakabase-Client", out var v) ? v.ToString() : null);
    }

    // ---- recycle bin ----

    [TestMethod]
    public async Task The_recycle_bin_is_refused_as_the_servers_rather_than_offered_as_a_mapping()
    {
        // Nothing the user can configure here makes this machine's recycle bin hold files
        // deleted on another one, so the answer must not read like a setup prompt.
        var context = Request();

        await new RecycleBinHandler().HandleAsync(context, new Dictionary<string, string>());

        var (status, body, header) = Read(context);
        Assert.AreEqual((int) HttpStatusCode.NotImplemented, status);
        Assert.IsNull(header, "Not a mapping failure — offering to map something would send the user nowhere.");

        var message = body.GetProperty("message").GetString()!;
        StringAssert.Contains(message, "server");
        Assert.IsFalse(message.Contains("path mapping", StringComparison.OrdinalIgnoreCase));
    }

    // ---- file icon ----

    [TestMethod]
    public async Task A_file_icon_is_answered_with_nothing_and_not_as_a_failure()
    {
        // The desktop app's own GUI adapter has always returned null here, so the
        // frontend already copes. Answering it as an error would be a regression the
        // all-in-one never had.
        var context = Request();

        await new FileIconHandler().HandleAsync(context, new Dictionary<string, string>());

        var (status, body, _) = Read(context);
        Assert.AreEqual((int) HttpStatusCode.OK, status);
        Assert.AreEqual(0, body.GetProperty("code").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, body.GetProperty("data").ValueKind);
    }

    // ---- tampermonkey ----

    [TestMethod]
    public async Task The_userscript_link_points_at_this_client_not_at_the_server()
    {
        // A userscript installed from the server's own address would run in a tab with no
        // device key and be refused on every call. Through the client, the forwarding
        // layer signs for it.
        var handler = new TampermonkeyInstallHandler(new StubLoopback("http://127.0.0.1:34568"), _shell,
            NullLogger<TampermonkeyInstallHandler>.Instance);

        var context = Request();
        await handler.HandleAsync(context, new Dictionary<string, string>());

        Assert.AreEqual((int) HttpStatusCode.OK, Read(context).Status);
        CollectionAssert.AreEqual(
            new[] {"http://127.0.0.1:34568/tampermonkey/script/bakabase.user.js"}, _shell.Launched);
    }

    [TestMethod]
    public async Task A_browser_that_will_not_open_is_reported_rather_than_reported_as_success()
    {
        _shell.LaunchThrows = new InvalidOperationException("no browser");

        var handler = new TampermonkeyInstallHandler(new StubLoopback("http://127.0.0.1:34568"), _shell,
            NullLogger<TampermonkeyInstallHandler>.Instance);

        var context = Request();
        await handler.HandleAsync(context, new Dictionary<string, string>());

        var (status, body, _) = Read(context);
        Assert.AreEqual((int) HttpStatusCode.InternalServerError, status);
        StringAssert.Contains(body.GetProperty("message").GetString()!, "no browser");
    }

    // ---- aigc artifact ----

    private OpenAigcArtifactHandler ArtifactHandler() =>
        new(_connection, _upstream, _shell, NullLogger<OpenAigcArtifactHandler>.Instance);

    [TestMethod]
    public async Task An_unreachable_server_is_told_apart_from_an_unmapped_library()
    {
        // Both end in "it did not open", and the fix is completely different: one is
        // "your server is down", the other "tell me where that folder is here".
        var context = Request();

        await ArtifactHandler().HandleAsync(context, new Dictionary<string, string> {["id"] = "7"});

        var (status, _, header) = Read(context);
        Assert.AreEqual((int) HttpStatusCode.ServiceUnavailable, status);
        Assert.IsNull(header);
        Assert.AreEqual(0, _shell.Revealed.Count);
    }

    [TestMethod]
    public async Task An_artifact_in_an_unmapped_library_is_refused_by_name()
    {
        await WithLibraryAt("/data/media");
        _upstream.ArtifactPath = "/srv/aigc/out/cover.png";

        var context = Request();
        await ArtifactHandler().HandleAsync(context, new Dictionary<string, string> {["id"] = "7"});

        var (status, body, header) = Read(context);
        Assert.AreEqual((int) HttpStatusCode.NotFound, status);
        Assert.AreEqual(nameof(ClientForwardingFailure.PathNotMapped), header);
        Assert.AreEqual("/srv/aigc/out/cover.png", body.GetProperty("serverPath").GetString());
        Assert.AreEqual(0, _shell.Revealed.Count);
    }

    [TestMethod]
    public async Task A_mapped_artifact_that_is_gone_here_is_not_revealed()
    {
        await WithLibraryAt("/data/media");
        _upstream.ArtifactPath = "/data/media/aigc/cover.png";

        var context = Request();
        await ArtifactHandler().HandleAsync(context, new Dictionary<string, string> {["id"] = "7"});

        Assert.AreEqual((int) HttpStatusCode.NotFound, Read(context).Status);
        Assert.AreEqual(0, _shell.Revealed.Count);
    }

    [TestMethod]
    public async Task A_mapped_artifact_is_revealed_at_its_local_path()
    {
        var local = await WithLibraryAt("/data/media");
        Directory.CreateDirectory(Path.Combine(local, "aigc"));
        await File.WriteAllTextAsync(Path.Combine(local, "aigc", "cover.png"), "x");
        _upstream.ArtifactPath = "/data/media/aigc/cover.png";

        var context = Request();
        await ArtifactHandler().HandleAsync(context, new Dictionary<string, string> {["id"] = "7"});

        Assert.AreEqual((int) HttpStatusCode.OK, Read(context).Status);
        Assert.AreEqual(1, _shell.Revealed.Count);
        Assert.AreEqual(Path.Combine(local, "aigc", "cover.png"), _shell.Revealed[0].Path);
        Assert.IsFalse(_shell.Revealed[0].InParent, "The artifact itself is the thing to show, not its folder.");
    }
}
