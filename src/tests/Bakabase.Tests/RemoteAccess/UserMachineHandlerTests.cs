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
/// What the client decides before it asks the operating system to open anything.
/// </summary>
/// <remarks>
/// Everything interesting about these handlers happens ahead of the shell call —
/// translating a path, refusing one that maps nowhere, falling back to a parent, telling
/// an unreachable server apart from an unmapped library. The shell itself is a seam, so
/// those decisions can be checked without spawning a file manager.
/// </remarks>
[TestClass]
public class UserMachineHandlerTests
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

        public void Reveal(string path, bool inParentDirectory) => Revealed.Add((path, inParentDirectory));
        public void Launch(string target) => Launched.Add(target);

        public void LaunchProcess(string executable, string arguments, bool useShellExecute) =>
            throw new NotSupportedException();

        public void LaunchProgram(string path, string? workingDirectory) =>
            throw new NotSupportedException();
    }

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-handler-tests", Guid.NewGuid().ToString("N"));
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

    /// <summary>Pairs with a server and maps one library onto a real temp directory.</summary>
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

    private static DefaultHttpContext Request(string queryString)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(queryString);
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

    // ---- opening a path ----

    [TestMethod]
    public async Task An_unmapped_path_is_refused_by_name_rather_than_guessed_at()
    {
        var handler = new OpenPathHandler(_connection, _shell, NullLogger<OpenPathHandler>.Instance);
        var context = Request("?path=/data/media/a.mkv");

        await handler.HandleAsync(context, new Dictionary<string, string>());

        var (status, body, header) = Read(context);
        Assert.AreEqual((int) HttpStatusCode.NotFound, status);
        Assert.AreEqual(nameof(ClientForwardingFailure.PathNotMapped), header);
        Assert.AreEqual("/data/media/a.mkv", body.GetProperty("serverPath").GetString());
        Assert.AreEqual(0, _shell.Revealed.Count);
    }

    [TestMethod]
    public async Task A_mapped_path_is_opened_after_translation()
    {
        var local = await WithLibraryAt("/data/media");
        var handler = new OpenPathHandler(_connection, _shell, NullLogger<OpenPathHandler>.Instance);
        var context = Request("?path=/data/media/anime/a.mkv&openInDirectory=true");

        await handler.HandleAsync(context, new Dictionary<string, string>());

        Assert.AreEqual((int) HttpStatusCode.OK, Read(context).Status);
        Assert.AreEqual(1, _shell.Revealed.Count);
        Assert.AreEqual(Path.Combine(local, "anime", "a.mkv"), _shell.Revealed[0].Path);
        Assert.IsTrue(_shell.Revealed[0].InParent);
    }

    // ---- opening a file ----

    [TestMethod]
    public async Task A_file_that_is_not_there_is_not_launched()
    {
        // Without this, a mapping pointed at the wrong root turns "open this video" into
        // starting whatever program happens to sit at the resulting path.
        await WithLibraryAt("/data/media");
        var handler = new OpenFileHandler(_connection, _shell, NullLogger<OpenFileHandler>.Instance);
        var context = Request("?path=/data/media/missing.mkv");

        await handler.HandleAsync(context, new Dictionary<string, string>());

        Assert.AreEqual((int) HttpStatusCode.NotFound, Read(context).Status);
        Assert.AreEqual(0, _shell.Launched.Count);
    }

    [TestMethod]
    public async Task A_folder_is_not_launched_as_a_file()
    {
        var local = await WithLibraryAt("/data/media");
        Directory.CreateDirectory(Path.Combine(local, "season"));

        var handler = new OpenFileHandler(_connection, _shell, NullLogger<OpenFileHandler>.Instance);
        var context = Request("?path=/data/media/season");

        await handler.HandleAsync(context, new Dictionary<string, string>());

        var (status, body, _) = Read(context);
        Assert.AreEqual((int) HttpStatusCode.NotFound, status);
        StringAssert.Contains(body.GetProperty("message").GetString()!, "folder");
        Assert.AreEqual(0, _shell.Launched.Count);
    }

    [TestMethod]
    public async Task A_file_that_is_there_is_launched()
    {
        var local = await WithLibraryAt("/data/media");
        await File.WriteAllTextAsync(Path.Combine(local, "a.mkv"), "x");

        var handler = new OpenFileHandler(_connection, _shell, NullLogger<OpenFileHandler>.Instance);
        var context = Request("?path=/data/media/a.mkv");

        await handler.HandleAsync(context, new Dictionary<string, string>());

        Assert.AreEqual((int) HttpStatusCode.OK, Read(context).Status);
        CollectionAssert.AreEqual(new[] {Path.Combine(local, "a.mkv")}, _shell.Launched);
    }

    // ---- opening a resource's folder ----

    private OpenResourceDirectoryHandler ResourceHandler() =>
        new(_connection, _upstream, _shell, NullLogger<OpenResourceDirectoryHandler>.Instance);

    [TestMethod]
    public async Task An_unreachable_server_is_reported_as_such_not_as_a_missing_mapping()
    {
        // The library lives over there, so this handler has to ask before it can act.
        // Blaming the mapping would send the user to configure something that is fine.
        var context = Request("?id=42");

        await ResourceHandler().HandleAsync(context, new Dictionary<string, string>());

        var (status, _, header) = Read(context);
        Assert.AreEqual((int) HttpStatusCode.ServiceUnavailable, status);
        Assert.IsNull(header);
    }

    [TestMethod]
    public async Task A_resource_with_no_path_cannot_be_opened()
    {
        _upstream.Resource = new UpstreamResource(42, null, null, "Nameless");
        var context = Request("?id=42");

        await ResourceHandler().HandleAsync(context, new Dictionary<string, string>());

        Assert.AreEqual((int) HttpStatusCode.BadRequest, Read(context).Status);
    }

    [TestMethod]
    public async Task A_resource_whose_folder_is_gone_here_falls_back_to_its_parent()
    {
        // The server's own fallback, but judged against this filesystem — a mount that
        // has gone stale on this machine looks perfectly healthy from the server.
        var local = await WithLibraryAt("/data/media");
        _upstream.Resource = new UpstreamResource(42, "/data/media/gone", "/data/media", "Show");

        var context = Request("?id=42");
        await ResourceHandler().HandleAsync(context, new Dictionary<string, string>());

        Assert.AreEqual((int) HttpStatusCode.OK, Read(context).Status);
        Assert.AreEqual(local, _shell.Revealed[0].Path);
    }

    [TestMethod]
    public async Task A_resource_with_nothing_left_to_fall_back_to_says_so()
    {
        await WithLibraryAt("/data/media");
        _upstream.Resource = new UpstreamResource(42, "/data/media/gone", "/data/media/also-gone", "Show");

        var context = Request("?id=42");
        await ResourceHandler().HandleAsync(context, new Dictionary<string, string>());

        var (status, body, _) = Read(context);
        Assert.AreEqual((int) HttpStatusCode.NotFound, status);
        StringAssert.Contains(body.GetProperty("message").GetString()!, "path mapping");
        Assert.AreEqual(0, _shell.Revealed.Count);
    }

    [TestMethod]
    public async Task A_resource_in_an_unmapped_library_is_refused_by_name()
    {
        await WithLibraryAt("/data/media");
        _upstream.Resource = new UpstreamResource(42, "/srv/other/Show", "/srv/other", "Show");

        var context = Request("?id=42");
        await ResourceHandler().HandleAsync(context, new Dictionary<string, string>());

        var (status, body, header) = Read(context);
        Assert.AreEqual((int) HttpStatusCode.NotFound, status);
        Assert.AreEqual(nameof(ClientForwardingFailure.PathNotMapped), header);
        Assert.AreEqual("/srv/other/Show", body.GetProperty("serverPath").GetString());
    }

    // ---- opening a link ----

    [TestMethod]
    public async Task A_web_link_is_opened_and_anything_else_is_not()
    {
        var handler = new OpenUrlHandler(_shell, NullLogger<OpenUrlHandler>.Instance);

        var good = Request("?url=https%3A%2F%2Fexample.com");
        await handler.HandleAsync(good, new Dictionary<string, string>());
        Assert.AreEqual((int) HttpStatusCode.OK, Read(good).Status);

        var bad = Request("?url=file%3A%2F%2F%2Fetc%2Fpasswd");
        await handler.HandleAsync(bad, new Dictionary<string, string>());
        Assert.AreEqual((int) HttpStatusCode.BadRequest, Read(bad).Status);

        CollectionAssert.AreEqual(new[] {"https://example.com"}, _shell.Launched);
    }
}
