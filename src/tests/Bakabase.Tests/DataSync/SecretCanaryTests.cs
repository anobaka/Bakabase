using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Tests.DataSync.Apply;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.Apply.DataSyncApplyFixture;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// §12 (product must-fix 15), the part this device's engine answers for: with the marker seeded into a
/// <c>Passwords</c> row, <c>AiProviders.ApiKey</c>, a third-party cookie option and the network proxy password, a
/// full reconciliation of every kind through the real feed (head, manifest, every page's bytes), an auto sync, an
/// inbox resolution and an undo leave the marker nowhere: not in the pages, the head or the manifest, not in any
/// data sync table (history details and inbox payloads included), not in the data sync folder, not in an undo
/// preview, and not in the Information-level log output. The test temp root never appears either.
/// </summary>
/// <remarks>
/// Federation's <c>state.json</c>, the remote-access and managed-connection keys, pairing by request, code and
/// read-back, and the <c>/data-sync/*</c> answers and notifications: the second test (SecretCanaryTests.Pairing.cs).
/// </remarks>
[TestClass]
public partial class SecretCanaryTests
{
    private const string Marker = "CANARY-7f1d";

    [TestMethod]
    public async Task The_marker_and_the_temp_root_never_leave_through_data_sync()
    {
        var logs = new CapturingLoggerProvider();
        var f = await CreateAsync(s =>
        {
            s.AddSingleton<ILoggerProvider>(logs);
            s.AddLogging(b => b.AddFilter<CapturingLoggerProvider>(null, LogLevel.Information));
        });
        await SeedSecretsAsync(f);
        logs.Clear();

        // Synced content of both kinds, made here.
        await f.AddGroupAsync("Video", ".mp4", ".mkv");
        f.Kind.Add(Content("Mood", ("m", "Calm")));
        await f.RefreshAsync();

        // An auto sync, a conflict resolved, and the resolution undone.
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);
        var key = SyncKey.New().Value;
        var v1 = peer.Next();
        var outcomes = new List<object>
        {
            await f.ApplyAsync(link, peer, (Item, peer.Record([key], v1, Content("Genre", ("a", "Rock")), "a1")))
        };
        var genre = f.Kind.KeyOf("Genre");
        f.Kind.Definitions[genre] = f.Kind[genre].With(name: "Style");
        outcomes.Add(await f.ApplyAsync(link, peer,
            (Item, peer.Record([key], peer.Next(v1), Content("Kind", ("a", "Rock")), "a1"))));
        var item = (await f.OpenItemsAsync()).Single();
        var resolution = await f.ResolveAsync(item, DataSyncInboxAction.UseRemote);
        Assert.IsNotNull(resolution);
        var preview = await f.Services.GetRequiredService<DataSyncUndoPlanner>().PreviewAsync(resolution.Value, default);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsNotNull(await f.UndoAsync(resolution.Value));

        // A full reconciliation of every kind through the real feed, as a reader reads it.
        var feed = f.Services.GetRequiredService<IDataSyncFeedSource>();
        var reader = DataSyncFeedFixture.Reader();
        var query = DataSyncFeedFixture.Query((Item, 0), (Groups, 0));
        var head = await feed.GetHeadAsync(reader, query, default);
        var manifest = await feed.CreateSnapshotAsync(reader, query, default);
        var pages = new List<byte[]>();
        foreach (var kind in manifest.Kinds)
            pages.AddRange((await DataSyncFeedFixture.ReadKindAsync(feed, reader, manifest, kind)).Pages);
        var served = DataSyncFeedFixture.Reassemble(pages);
        Assert.IsTrue(served.Any(r => r["keys"]![0]!.GetValue<string>() == key), "the feed served the synced entity");

        f.Services.GetRequiredService<ILogger<SecretCanaryTests>>().LogInformation("capture probe");
        StringAssert.Contains(logs.Text, "capture probe", "Information-level output is captured");

        var exposed = new Dictionary<string, string>
        {
            ["pages"] = string.Join("\n", pages.Select(p => Encoding.UTF8.GetString(p))),
            ["head"] = JsonSerializer.Serialize(head),
            ["manifest"] = JsonSerializer.Serialize(manifest),
            ["outcomes"] = JsonSerializer.Serialize(outcomes),
            ["undo preview"] = JsonSerializer.Serialize(preview),
            ["tables"] = await DataSyncTablesAsync(f),
            ["folder"] = FolderText(f.Directory.Path),
            ["log"] = logs.Text,
        };
        StringAssert.Contains(exposed["tables"], key, "the dump reads the rows");
        var root = Path.GetDirectoryName(f.Directory.Path)!;
        foreach (var (what, text) in exposed)
        {
            Assert.IsFalse(text.Contains(Marker, StringComparison.OrdinalIgnoreCase), $"the marker is in the {what}");
            foreach (var path in new[] { root, root.Replace('\\', '/'), f.Directory.BackupsPath })
                Assert.IsFalse(text.Contains(path, StringComparison.OrdinalIgnoreCase), $"the temp root is in the {what}");
        }
    }

    private static async Task SeedSecretsAsync(DataSyncApplyFixture f)
    {
        var db = f.NewDb();
        db.Passwords.Add(new PasswordDbModel { Text = Marker + "-password", LastUsedAt = DateTime.Now });
        db.AiProviders.Add(new AiProviderDbModel { Name = "Provider", ApiKey = Marker + "-api-key" });
        await db.SaveChangesAsync();
        await f.Services.GetRequiredService<IBOptionsManager<ExHentaiOptions>>()
            .SaveAsync(o => o.Cookie = "ipb_pass_hash=" + Marker);
        await f.Services.GetRequiredService<IBOptionsManager<NetworkOptions>>().SaveAsync(o => o.CustomProxies =
        [
            new NetworkOptions.ProxyOptions
            {
                Id = "proxy-1", Address = "http://127.0.0.1:1",
                Credentials = new NetworkOptions.ProxyOptions.ProxyCredentials { Username = "u", Password = Marker }
            }
        ]);
        Assert.AreEqual(1, await f.NewDb().Passwords.CountAsync(p => p.Text.Contains(Marker)), "seeded");
    }

    /// <summary>Every row of every data sync table, every column as text.</summary>
    private static async Task<string> DataSyncTablesAsync(DataSyncApplyFixture f)
    {
        var text = new StringBuilder();
        await using var connection = new SqliteConnection(f.NewDb().Database.GetConnectionString());
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE 'DataSync%'";
            await using var names = await list.ExecuteReaderAsync();
            while (await names.ReadAsync()) tables.Add(names.GetString(0));
        }

        Assert.IsTrue(tables.Count >= 8, "the data sync tables");
        foreach (var table in tables)
        {
            await using var select = connection.CreateCommand();
            select.CommandText = $"SELECT * FROM \"{table}\"";
            await using var rows = await select.ExecuteReaderAsync();
            while (await rows.ReadAsync())
            {
                text.Append(table).Append(':');
                for (var i = 0; i < rows.FieldCount; i++) text.Append(' ').Append(rows.GetValue(i));
                text.AppendLine();
            }
        }

        return text.ToString();
    }

    private static string FolderText(string path) =>
        Directory.Exists(path)
            ? string.Join("\n", Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Select(file => file + "\n" + File.ReadAllText(file)).Select(t => t[(path.Length)..]))
            : "";

    /// <summary>Information-level output of every category, as the app's log would hold it.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public string Text => string.Join("\n", _lines);

        public void Clear() => _lines.Clear();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                owner._lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
            }
        }
    }
}
