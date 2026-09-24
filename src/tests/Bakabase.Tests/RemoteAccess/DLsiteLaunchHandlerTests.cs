using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
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
/// Running a downloaded work on this machine.
/// </summary>
/// <remarks>
/// The one user-machine action with no streaming fallback: a program runs from real
/// files, so a work this computer cannot reach genuinely cannot be started here. Getting
/// that refusal right — and not quietly launching something else instead — is what these
/// check.
/// </remarks>
[TestClass]
public class DLsiteLaunchHandlerTests
{
    private string _root = null!;
    private ActiveConnection _connection = null!;
    private RecordingShell _shell = null!;
    private StubUpstreamApi _upstream = null!;
    private StubLocaleEmulator _localeEmulator = null!;

    private sealed class TempDirectory(string path) : IClientDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    private sealed class RecordingShell : IShellOpener
    {
        public readonly List<(string Path, string? WorkingDirectory)> Programs = [];
        public readonly List<(string Executable, string Arguments, bool Shell)> Processes = [];

        public void Reveal(string path, bool inParentDirectory) => throw new NotSupportedException();
        public void Launch(string target) => throw new NotSupportedException();

        public void LaunchProcess(string executable, string arguments, bool useShellExecute) =>
            Processes.Add((executable, arguments, useShellExecute));

        public void LaunchProgram(string path, string? workingDirectory) =>
            Programs.Add((path, workingDirectory));
    }

    private sealed class StubLocaleEmulator : ILocaleEmulatorLauncher
    {
        public bool IsAvailable { get; set; }
        public Exception? Throws;
        public readonly List<string> Launched = [];

        public Task LaunchAsync(string executablePath, CancellationToken ct)
        {
            if (Throws != null)
            {
                return Task.FromException(Throws);
            }

            Launched.Add(executablePath);

            return Task.CompletedTask;
        }
    }

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-dlsite-launch", Guid.NewGuid().ToString("N"));
        _connection = new ActiveConnection(new ClientConnectionStore(new TempDirectory(_root)));
        _shell = new RecordingShell();
        _upstream = new StubUpstreamApi();
        _localeEmulator = new StubLocaleEmulator();
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

    private DLsiteLaunchHandler Handler() =>
        new(_connection, _upstream, _shell, _localeEmulator, NullLogger<DLsiteLaunchHandler>.Instance);

    private Task Launch(HttpContext context, string workId = "RJ01234567") =>
        Handler().HandleAsync(context, new Dictionary<string, string> {["workId"] = workId});

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

    /// <summary>Maps /downloads onto a real temp folder and puts one file in it.</summary>
    private async Task<string> WithDownloadedFile(string name)
    {
        var local = Path.Combine(_root, "downloads");
        Directory.CreateDirectory(local);
        await File.WriteAllTextAsync(Path.Combine(local, name), "x");

        await _connection.SaveAsync("server-1", "Desk", "http://192.168.1.5:34567",
            new ClientCredentials("d1", "k1"), DateTime.UtcNow);

        await _connection.SetPathMappingsAsync("server-1",
            [new ClientPathMapping {ServerPath = "/downloads", LocalPath = local}]);

        return local;
    }

    [TestMethod]
    public async Task An_unreachable_server_is_not_reported_as_a_missing_mapping()
    {
        var context = Request();

        await Launch(context);

        var (status, _, header) = Read(context);
        Assert.AreEqual((int) HttpStatusCode.ServiceUnavailable, status);
        Assert.IsNull(header);
        Assert.AreEqual(0, _shell.Programs.Count);
    }

    [TestMethod]
    public async Task A_work_in_an_unmapped_folder_is_refused_rather_than_streamed()
    {
        // Everything else the client opens can come down a URL. A program cannot: it
        // needs its data files, its DLLs, somewhere to write. Refusing is the honest
        // answer, and it names the folder so the user can map it.
        await WithDownloadedFile("game.exe");
        _upstream.DLsiteLaunchTarget = new DLsiteWorkLaunchTarget("/srv/elsewhere/game.exe", true, false);

        var context = Request();
        await Launch(context);

        var (status, body, header) = Read(context);
        Assert.AreEqual((int) HttpStatusCode.NotFound, status);
        Assert.AreEqual(nameof(ClientForwardingFailure.PathNotMapped), header);
        Assert.AreEqual("/srv/elsewhere/game.exe", body.GetProperty("serverPath").GetString());
        Assert.AreEqual(0, _shell.Programs.Count);
    }

    [TestMethod]
    public async Task A_mapped_work_that_is_not_actually_here_says_so_and_starts_nothing()
    {
        await WithDownloadedFile("game.exe");
        _upstream.DLsiteLaunchTarget = new DLsiteWorkLaunchTarget("/downloads/gone.exe", true, false);

        var context = Request();
        await Launch(context);

        var (status, body, _) = Read(context);
        Assert.AreEqual((int) HttpStatusCode.NotFound, status);
        StringAssert.Contains(body.GetProperty("message").GetString()!, "path mapping");
        Assert.AreEqual(0, _shell.Programs.Count);
    }

    [TestMethod]
    public async Task A_program_starts_from_its_own_folder()
    {
        // A game that reads its data with relative paths finds nothing when started from
        // somewhere else.
        var local = await WithDownloadedFile("game.exe");
        _upstream.DLsiteLaunchTarget = new DLsiteWorkLaunchTarget("/downloads/game.exe", true, false);

        var context = Request();
        await Launch(context);

        Assert.AreEqual((int) HttpStatusCode.OK, Read(context).Status);
        Assert.AreEqual((Path.Combine(local, "game.exe"), local), _shell.Programs[0]);
    }

    [TestMethod]
    public async Task A_document_is_opened_without_forcing_a_working_directory()
    {
        var local = await WithDownloadedFile("track.mp3");
        _upstream.DLsiteLaunchTarget = new DLsiteWorkLaunchTarget("/downloads/track.mp3", false, false);

        var context = Request();
        await Launch(context);

        Assert.AreEqual((Path.Combine(local, "track.mp3"), null), _shell.Programs[0]);
    }

    // ---- locale emulator ----

    [TestMethod]
    public async Task A_work_that_asked_for_locale_emulation_gets_it_when_it_is_installed_here()
    {
        var local = await WithDownloadedFile("game.exe");
        _upstream.DLsiteLaunchTarget = new DLsiteWorkLaunchTarget("/downloads/game.exe", true, true);
        _localeEmulator.IsAvailable = true;

        var context = Request();
        await Launch(context);

        CollectionAssert.AreEqual(new[] {Path.Combine(local, "game.exe")}, _localeEmulator.Launched);
        Assert.AreEqual(0, _shell.Programs.Count);
    }

    [TestMethod]
    public async Task A_work_still_runs_when_locale_emulator_is_missing_or_fails()
    {
        // The server falls back to a direct launch in both cases, and a Japanese game
        // starting with the wrong locale beats one that does not start.
        var local = await WithDownloadedFile("game.exe");
        _upstream.DLsiteLaunchTarget = new DLsiteWorkLaunchTarget("/downloads/game.exe", true, true);

        var notInstalled = Request();
        await Launch(notInstalled);

        Assert.AreEqual((int) HttpStatusCode.OK, Read(notInstalled).Status);
        Assert.AreEqual(Path.Combine(local, "game.exe"), _shell.Programs[0].Path);

        _localeEmulator.IsAvailable = true;
        _localeEmulator.Throws = new InvalidOperationException("LEProc exited 1");

        var failed = Request();
        await Launch(failed);

        Assert.AreEqual((int) HttpStatusCode.OK, Read(failed).Status);
        Assert.AreEqual(2, _shell.Programs.Count);
    }

    [TestMethod]
    public async Task A_document_is_never_locale_emulated()
    {
        // Only a program has a locale to emulate; handing LEProc an mp3 achieves nothing.
        await WithDownloadedFile("track.mp3");
        _upstream.DLsiteLaunchTarget = new DLsiteWorkLaunchTarget("/downloads/track.mp3", false, true);
        _localeEmulator.IsAvailable = true;

        var context = Request();
        await Launch(context);

        Assert.AreEqual(0, _localeEmulator.Launched.Count);
        Assert.AreEqual(1, _shell.Programs.Count);
    }
}
