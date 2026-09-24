using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Remoting.Abstractions;
using Bakabase.Client.Remoting.Components.Diagnostics;
using Bakabase.Remoting.Components.Diagnostics;
using Bakabase.Service.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// What the client reports about itself, and the two things that would silently rot.
/// </summary>
/// <remarks>
/// The client cannot reference <c>Bakabase.Service</c>, so its settings file and its
/// release-channel rule are copies of the server's. This test assembly references both,
/// which makes it the only place the copies can be held together.
/// </remarks>
[TestClass]
public class ClientTelemetryTests
{
    [TestMethod]
    public void Both_flavours_report_into_the_same_project()
    {
        // Two DSNs would split one product's errors across two dashboards, and nobody
        // would notice until they went looking for a crash that was filed elsewhere.
        Assert.AreEqual(DsnFrom("appsettings.json", "BackendDsn"),
            DsnFrom(ClientTelemetry.SettingsFileName, "ClientDsn"),
            "the client's settings file has drifted from the server's");
    }

    [TestMethod]
    public void The_two_files_are_separate_files()
    {
        // Both land in this test's output directory, and one named appsettings.json would
        // overwrite the other — whichever copied last winning, with no warning anywhere.
        Assert.AreNotEqual("appsettings.json", ClientTelemetry.SettingsFileName);
        Assert.IsTrue(File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        Assert.IsTrue(File.Exists(Path.Combine(AppContext.BaseDirectory, ClientTelemetry.SettingsFileName)));
    }

    [TestMethod]
    public void The_servers_settings_alone_never_switch_the_client_on()
    {
        // The one that matters. Both files sit beside this test, and a host built from
        // ClientStartup reads whatever configuration it was given — which for every test
        // host is the appsettings.json next to it, the server's. Sharing the key would
        // have each of those initialise the real SDK and report test runs into the live
        // project, which is exactly what happened before the keys were split.
        var serversFile = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .Build();

        Assert.IsNotNull(serversFile["Analytics:Sentry:BackendDsn"],
            "if this is null the test proves nothing — the server's own DSN moved");
        Assert.IsFalse(ClientTelemetry.IsEnabled(serversFile, false));
    }

    [TestMethod]
    public void The_channel_rule_matches_the_servers()
    {
        // Same build, same answer. A client reporting "beta" where the server reports
        // "stable" makes every dashboard split by channel lie.
        Assert.AreEqual(ReleaseChannelDetector.Detect(Environment("Development")),
            ClientTelemetry.ReleaseChannel(true));

        Assert.AreEqual(ReleaseChannelDetector.Detect(Environment("Production")),
            ClientTelemetry.ReleaseChannel(false));
    }

    [TestMethod]
    public void Reporting_is_off_where_there_is_nothing_to_report_to()
    {
        var configured = Configuration((ClientTelemetry.DsnConfigurationKey, "https://k@example.test/1"));

        Assert.IsTrue(ClientTelemetry.IsEnabled(configured, false));

        // A development build reports into its developer's console, not into the
        // dashboard everyone else's crashes land in.
        Assert.IsFalse(ClientTelemetry.IsEnabled(configured, true));

        // And a host with no settings file at all — every test host, including the ones
        // in this assembly — reports nowhere.
        Assert.IsFalse(ClientTelemetry.IsEnabled(Configuration(), false));
        Assert.IsFalse(ClientTelemetry.IsEnabled(
            Configuration((ClientTelemetry.DsnConfigurationKey, "   ")), false));
    }

    [TestMethod]
    public void The_failures_of_being_on_the_far_side_of_a_network_are_not_reported()
    {
        // A thin client talks to a machine on somebody's LAN. If these were reported,
        // one laptop closing its lid would file more events than every real defect
        // together, and the real ones would never be found.
        Assert.IsFalse(ClientTelemetry.ShouldReport(new HttpRequestException("no route")));
        Assert.IsFalse(ClientTelemetry.ShouldReport(new SocketException(111)));
        Assert.IsFalse(ClientTelemetry.ShouldReport(new OperationCanceledException()));

        // Wrapped, which is how they nearly always arrive.
        Assert.IsFalse(ClientTelemetry.ShouldReport(
            new InvalidOperationException("forwarding failed", new SocketException(111))));

        // Something the user fixes themselves, marked at the type as on the server.
        Assert.IsFalse(ClientTelemetry.ShouldReport(new UserFixable()));

        // And an actual defect, which is the entire point of the thing.
        Assert.IsTrue(ClientTelemetry.ShouldReport(new NullReferenceException()));
        Assert.IsTrue(ClientTelemetry.ShouldReport(null), "an event with no exception is a log event");
    }

    [TestMethod]
    public void The_anonymous_id_survives_a_restart_and_belongs_to_one_install()
    {
        var first = new TempDirectory();
        var second = new TempDirectory();

        try
        {
            var id = ClientAnonymousId.GetOrCreate(first);

            Assert.IsTrue(Guid.TryParse(id, out _));
            Assert.AreEqual(id, ClientAnonymousId.GetOrCreate(first), "a restart is not a new install");

            // Which is also why the all-in-one's id is never this one: it keeps its own
            // file under its own application data directory, and the two flavours never
            // share a directory.
            Assert.AreNotEqual(id, ClientAnonymousId.GetOrCreate(second));
        }
        finally
        {
            first.Delete();
            second.Delete();
        }
    }

    [TestMethod]
    public void A_malformed_id_is_replaced_rather_than_reported()
    {
        var directory = new TempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(directory.Ensure(), ClientAnonymousId.FileName), "not a uuid");

            Assert.IsTrue(Guid.TryParse(ClientAnonymousId.GetOrCreate(directory), out _));
        }
        finally
        {
            directory.Delete();
        }
    }

    private static string? DsnFrom(string fileName, string key) =>
        JsonDocument
            .Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, fileName)),
                // Both files carry comments explaining what the values are for.
                new JsonDocumentOptions {CommentHandling = JsonCommentHandling.Skip})
            .RootElement.GetProperty("Analytics").GetProperty("Sentry").GetProperty(key)
            .GetString();

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static IWebHostEnvironment Environment(string name) => new FakeEnvironment {EnvironmentName = name};

    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Bakabase.Tests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class UserFixable : Exception, IUserActionableException;

    private sealed class TempDirectory : IClientDataDirectory
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"bakabase-client-id-{Guid.NewGuid():N}");

        public string Ensure() => Directory.CreateDirectory(Path).FullName;

        public void Delete()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (Exception e) when (e is IOException or DirectoryNotFoundException)
            {
            }
        }
    }
}
