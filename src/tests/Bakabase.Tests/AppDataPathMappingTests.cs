using System.Text;
using Bakabase.Service.Components.ServerData;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests;

[TestClass]
public class AppDataPathMappingTests
{
    private string _root = null!;
    private string Database => Path.Combine(_root, "library.db");
    private static readonly PathMappingRule[] Rules = [new("Y:/", "/nas"), new("Y:/Use", "/nas/Use"), new("C:/Data", "/data")];

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "bakabase-path-map-" + Guid.NewGuid().ToString("N")));
    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void ScanIsReadOnlyAndPreviewUsesOnlyCapturedData()
    {
        Execute("CREATE TABLE Resources(Id INTEGER PRIMARY KEY,Path TEXT,Options TEXT); INSERT INTO Resources VALUES(1,'Y:/book','[\"C:/Data/a.jpg\"]');");
        var config = Path.Combine(_root, "configs", "fs.json");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, "{\"destination\":\"y:\\\\book\",\"prose\":\"See Y:/book\",\"url\":\"https://host/Y:/book\"}");
        var before = Snapshot();
        var reports = new List<PathMappingProgress>();
        var scan = AppDataPathMapping.Scan(_root, progress: reports.Add);
        EqualFiles(before);
        Assert.AreEqual(1, scan.DatabaseCount);
        Assert.AreEqual(1, scan.ConfigurationFileCount);
        Assert.AreEqual(2, scan.Paths.Count);
        var book = scan.Paths.Single(p => p.Path == "Y:/book");
        Assert.AreEqual(2L, book.ReferenceCount);
        CollectionAssert.Contains(book.Locations, "library.db:Resources.Path");
        Assert.IsTrue(reports.Count > 0 && reports[^1].PathReferences == 3);
        File.Delete(Database);
        File.Delete(config);
        var preview = AppDataPathMapping.Preview(scan, Rules);
        Assert.AreEqual(2, preview.MatchedPaths);
        Assert.AreEqual(3L, preview.MatchedReferences);
        Assert.AreEqual(0, preview.UnmappedPaths);
    }

    [TestMethod]
    public void LongestBoundaryCaseRulesSupportBothTargetPlatformsAndUnc()
    {
        var scan = ScanValues("y:\\USE\\目录", "Y:/TBD/a", "C:/Database/a", "/Case/A", "/case/A", @"\\server\share\a", "/slash\\name");
        PathMappingRule[] rules = [new("Y:/", "/nas"), new("Y:/use", "/nas/use"), new("C:/Data", "/data"),
            new("/Case", @"E:\Media"), new(@"\\SERVER\SHARE", @"\\new\media"), new("/slash\\name", "Z:/literal")];
        var preview = AppDataPathMapping.Preview(scan, rules);
        Assert.AreEqual(5, preview.MatchedPaths);
        Assert.AreEqual(2, preview.UnmappedPaths);
        CollectionAssert.AreEquivalent(new[] { "/nas/use/目录", "/nas/TBD/a", "E:/Media/A", "//new/media/a", "Z:/literal" }, preview.Examples.Select(e => e.TargetPath).ToArray());
        Assert.ThrowsExactly<InvalidDataException>(() => AppDataPathMapping.Preview(scan, [new("/", "Q:/")]),
            "Case-distinct POSIX names must not collapse onto case-insensitive Windows destinations.");
        var rootRule = AppDataPathMapping.Preview(ScanValues("Y:/one", "//server/share/a", "/posix/one"), [new("/", "Q:/")]);
        Assert.AreEqual(1, rootRule.MatchedPaths, "POSIX / must not capture UNC or drive paths.");
    }

    [TestMethod]
    public void NestedJsonKeysValuesEncodedJsonStandardValuesAndLegacyHistories()
    {
        Execute("CREATE TABLE CustomProperties(Id INTEGER PRIMARY KEY,Type INTEGER); INSERT INTO CustomProperties VALUES(1,10);" +
                "CREATE TABLE CustomPropertyValues(Id INTEGER PRIMARY KEY,PropertyId INTEGER,Value TEXT);" +
                "CREATE TABLE Enhancements(Id INTEGER PRIMARY KEY,ValueType INTEGER,Value TEXT);" +
                "CREATE TABLE PlayHistories(Id INTEGER PRIMARY KEY,Item TEXT);" +
                "CREATE TABLE ResourceCaches(Id INTEGER PRIMARY KEY,CoverPaths TEXT,PlayableFilePaths TEXT);" +
                "CREATE TABLE Resources(Id INTEGER PRIMARY KEY,Path TEXT);" +
                "CREATE TABLE MediaLibrariesV2(Id INTEGER PRIMARY KEY,Paths TEXT);");
        var list = Join(["Y:/one,two/a.jpg", "Y:/next/image.jpg", "https://host/a,b", "plain\\text"], ',');
        var nested = Join([list, Join(["C:/Data/semi;colon", "tail"], ',')], ';');
        Execute("INSERT INTO CustomPropertyValues VALUES(1,1,$v)", list);
        Execute("INSERT INTO Enhancements VALUES(1,8,$v)", nested);
        Execute("INSERT INTO PlayHistories VALUES(1,$v)", "FileSystem:Y:/one,two/a.jpg");
        Execute("INSERT INTO ResourceCaches VALUES(1,$v,'[\"Y:/json-array.jpg\"]')", list);
        Execute("INSERT INTO Resources VALUES(1,$v)", "Y:/one,two/a.jpg");
        Execute("INSERT INTO MediaLibrariesV2 VALUES(1,$v)", @"Y:\Use\one|Y:/other");
        var configuration = new JObject
        {
            ["Y:/json-key"] = new JArray("Y:/json-value", new JObject { ["ValueType"] = 2, ["Value"] = list }),
            ["encoded"] = "{\"Path\":\"C:/Data/inside\"}",
            ["date"] = "2026-10-09T05:00:00Z", ["plain"] = "See Y:/x", ["url"] = "https://host/Y:/x"
        };
        File.WriteAllText(Path.Combine(_root, "app.json"), configuration.ToString());
        var scan = AppDataPathMapping.Scan(_root);
        var preview = AppDataPathMapping.Preview(scan, Rules);
        var result = AppDataPathMapping.Apply(_root, Rules);
        Assert.AreEqual(preview.MatchedReferences, result.ChangedReferences);
        Assert.AreEqual(1, result.ChangedDatabases);
        Assert.AreEqual(1, result.ChangedConfigurationFiles);
        Assert.AreEqual("/nas/one,two/a.jpg", Scalar("SELECT Path FROM Resources"));
        Assert.AreEqual("FileSystem:/nas/one,two/a.jpg", Scalar("SELECT Item FROM PlayHistories"));
        Assert.AreEqual(Join(["/nas/one,two/a.jpg", "/nas/next/image.jpg", "https://host/a,b", "plain\\text"], ','), Scalar("SELECT Value FROM CustomPropertyValues"));
        StringAssert.Contains(Scalar("SELECT Value FROM Enhancements"), "/data/semi\\;colon");
        Assert.AreEqual("/nas/Use/one|/nas/other", Scalar("SELECT Paths FROM MediaLibrariesV2"));
        Assert.AreEqual("[\"/nas/json-array.jpg\"]", Scalar("SELECT PlayableFilePaths FROM ResourceCaches"));
        var mapped = JObject.Parse(File.ReadAllText(Path.Combine(_root, "app.json")));
        Assert.IsNotNull(mapped["/nas/json-key"]);
        Assert.AreEqual("/nas/json-value", (string?)mapped["/nas/json-key"]![0]);
        Assert.AreEqual("/data/inside", (string?)JObject.Parse((string)mapped["encoded"]!)["Path"]);
        Assert.AreEqual(configuration["plain"]!.ToString(), mapped["plain"]!.ToString());
        Assert.AreEqual(configuration["url"]!.ToString(), mapped["url"]!.ToString());
        var bytes = Snapshot();
        var repeated = AppDataPathMapping.Apply(_root, Rules);
        Assert.AreEqual(0L, repeated.ChangedReferences);
        Assert.AreEqual(0L, repeated.ChangedValues);
        EqualFiles(bytes);
    }

    [TestMethod]
    public void EveryRuntimeTextColumnAndWithoutRowidPrimaryKeysAreHandled()
    {
        Execute("CREATE TABLE Dynamic(Id INTEGER PRIMARY KEY,Value BLOB); INSERT INTO Dynamic VALUES(1,'Y:/dynamic');" +
                "CREATE TABLE \"odd\"\"table\"(\"str\"\"key\" TEXT PRIMARY KEY,Value TEXT) WITHOUT ROWID;" +
                "INSERT INTO \"odd\"\"table\" VALUES('Y:/key','Y:/value');");
        var result = AppDataPathMapping.Apply(_root, Rules);
        Assert.AreEqual(3L, result.ChangedValues);
        Assert.AreEqual("/nas/dynamic", Scalar("SELECT Value FROM Dynamic"));
        Assert.AreEqual("/nas/key", Scalar("SELECT \"str\"\"key\" FROM \"odd\"\"table\""));
        Assert.AreEqual("ok", Scalar("PRAGMA integrity_check"));
    }

    [TestMethod]
    public void PreviewAndApplyRejectPathAndJsonKeyCollisionsWithoutPartialDatabaseWrites()
    {
        Execute("CREATE TABLE Paths(Id INTEGER PRIMARY KEY,Value TEXT); INSERT INTO Paths VALUES(1,'Y:/first'),(2,'Y:/same'),(3,'/nas/same');");
        var scan = AppDataPathMapping.Scan(_root);
        Assert.ThrowsExactly<InvalidDataException>(() => AppDataPathMapping.Preview(scan, Rules));
        Assert.ThrowsExactly<InvalidDataException>(() => AppDataPathMapping.Apply(_root, Rules));
        Assert.AreEqual("Y:/first", Scalar("SELECT Value FROM Paths WHERE Id=1"));
        File.Delete(Database);
        var config = Path.Combine(_root, "app.json");
        const string original = "{\"Y:/same\":1,\"/nas/same\":2}";
        File.WriteAllText(config, original);
        Assert.ThrowsExactly<InvalidDataException>(() => AppDataPathMapping.Apply(_root, Rules));
        Assert.AreEqual(original, File.ReadAllText(config));
    }

    [TestMethod]
    public void UniqueConstraintFailureRollsBackTheWholeDatabase()
    {
        Execute("CREATE TABLE Paths(Id INTEGER PRIMARY KEY,Value TEXT UNIQUE COLLATE NOCASE); INSERT INTO Paths VALUES(1,'Y:/first'),(2,'Y:/same'),(3,'/NAS/SAME');");
        Assert.ThrowsExactly<SqliteException>(() => AppDataPathMapping.Apply(_root, Rules));
        Assert.AreEqual("Y:/first", Scalar("SELECT Value FROM Paths WHERE Id=1"));
        Assert.AreEqual("Y:/same", Scalar("SELECT Value FROM Paths WHERE Id=2"));
    }

    [TestMethod]
    [DataRow("Y:/", "/nas", "y:\\", "/other")]
    [DataRow("Y:/", "/same", "Z:/", "/same")]
    [DataRow("/a", "/b", "/b", "/a")]
    [DataRow("relative", "/x", "Y:/", "/y")]
    [DataRow("Y:/", "https://host", "Z:/", "/z")]
    public void InvalidRulesAreRejected(string from, string to, string from2, string to2) =>
        Assert.ThrowsExactly<ArgumentException>(() => AppDataPathMapping.ValidateRules([new(from, to), new(from2, to2)]));

    [TestMethod]
    public void ConfigurationScopeExcludesControlsBookkeepingBackupsAndMedia()
    {
        Execute("CREATE TABLE P(Value TEXT); INSERT INTO P VALUES('Y:/library');");
        foreach (var relative in new[] { "app.json", "configs/nested/settings.json", "downloader/a.json", "federation/a.json", "remote-access/a.json",
                     ".bakabase-setup-draft.json", "appdata-import-roots.json", "backups/old.json", "data/attachment.json", "components/a.json", "random-media.json.txt" })
        {
            var file = Path.Combine(_root, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "{\"path\":\"Y:/configuration\"}");
        }
        var nestedDb = Path.Combine(_root, "user-store", "nested.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedDb)!);
        File.Copy(Database, nestedDb);
        var files = AppDataPathMapping.InputFiles(_root).Select(p => Path.GetRelativePath(_root, p)).ToArray();
        CollectionAssert.AreEquivalent(new[] { "library.db", "app.json", "configs/nested/settings.json", "downloader/a.json", "federation/a.json", "remote-access/a.json", "user-store/nested.sqlite3" }, files);
        var before = File.ReadAllText(Path.Combine(_root, "appdata-import-roots.json"));
        var result = AppDataPathMapping.Apply(_root, Rules);
        Assert.AreEqual(2, result.ChangedDatabases);
        Assert.AreEqual(5, result.ChangedConfigurationFiles);
        Assert.AreEqual(before, File.ReadAllText(Path.Combine(_root, "appdata-import-roots.json")));
    }

    [TestMethod]
    public void LiveWalAndSymbolicLinksAreRejectedWithoutCreatingSidecars()
    {
        Execute("CREATE TABLE P(Value TEXT); INSERT INTO P VALUES('Y:/one');");
        using (var connection = Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; INSERT INTO P VALUES('Y:/two');";
            command.ExecuteNonQuery();
            Assert.ThrowsExactly<InvalidDataException>(() => AppDataPathMapping.Scan(_root));
            Assert.ThrowsExactly<InvalidDataException>(() => AppDataPathMapping.InputFiles(_root));
        }
        var before = Snapshot();
        AppDataPathMapping.Scan(_root);
        EqualFiles(before);
        if (OperatingSystem.IsWindows()) return;
        File.CreateSymbolicLink(Path.Combine(_root, "linked.db"), Database);
        Assert.ThrowsExactly<IOException>(() => AppDataPathMapping.Scan(_root));
    }

    [TestMethod]
    public void CancellationAndProgressFailureRollBackWithoutInstallingPartialChanges()
    {
        Execute("CREATE TABLE P(Value TEXT); INSERT INTO P VALUES('Y:/one'),('Y:/two');");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AppDataPathMapping.Scan(_root, cancelled.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => AppDataPathMapping.Apply(_root, Rules, cancelled.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => AppDataPathMapping.Apply(_root, Rules, progress: update =>
        { if (update.Phase == "mapping" && update.ScannedTextValues == 2 && update.Table == null)
            throw new OperationCanceledException("simulate cancellation after writes, before commit"); }));
        Assert.AreEqual("Y:/one", Scalar("SELECT Value FROM P LIMIT 1"));
    }

    [TestMethod]
    [DataRow(384)] // 0600: private credentials.
    [DataRow(432)] // 0660: preserve explicitly granted group access despite umask.
    public void ChangedConfigurationRetainsPrivatePermissions(int permissions)
    {
        if (OperatingSystem.IsWindows()) return;
        var file = Path.Combine(_root, "app.json");
        File.WriteAllText(file, "{\"path\":\"Y:/one\"}");
        File.SetUnixFileMode(file, (UnixFileMode)permissions);
        AppDataPathMapping.Apply(_root, Rules);
        Assert.AreEqual((UnixFileMode)permissions, File.GetUnixFileMode(file));
        Assert.AreEqual(1, Directory.GetFiles(_root).Length);
    }

    [TestMethod]
    public void LargeRepeatedReferencesAggregateAndExamplesAreBounded()
    {
        Execute("CREATE TABLE P(Value TEXT); WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<5000) INSERT INTO P SELECT 'Y:/item-'||(x%30) FROM n;");
        var scan = AppDataPathMapping.Scan(_root);
        Assert.AreEqual(30, scan.Paths.Count);
        Assert.AreEqual(5000L, scan.Paths.Sum(p => p.ReferenceCount));
        var preview = AppDataPathMapping.Preview(scan, Rules);
        Assert.AreEqual(5000L, preview.MatchedReferences);
        Assert.AreEqual(20, preview.Examples.Count);
    }

    [TestMethod]
    [DataRow("/Volumes/nas/Root", "/Volumes/nas", "/Volumes/nas/one")]
    [DataRow("/Volumes/nas/Root", "/Volumes/nas/Root/child", "/Volumes/nas/Root/child/one")]
    public void ASingleRuleMayMoveToParentOrChildExactlyOnce(string source, string target, string expected)
    {
        Execute("CREATE TABLE P(Value TEXT)");
        Execute("INSERT INTO P VALUES($v)", source + "/one");
        PathMappingRule[] rules = [new(source, target)];
        Assert.AreEqual(1, AppDataPathMapping.Preview(AppDataPathMapping.Scan(_root), rules).MatchedPaths);
        AppDataPathMapping.Apply(_root, rules);
        Assert.AreEqual(expected, Scalar("SELECT Value FROM P"));
    }

    [TestMethod]
    public void CollisionPreflightPreventsEarlierConfigurationOrDatabaseChanges()
    {
        File.WriteAllText(Path.Combine(_root, "app.json"), "{\"path\":\"Y:/first\"}");
        Execute("CREATE TABLE P(Value TEXT); INSERT INTO P VALUES('Y:/same'),('/nas/same');");
        var before = Snapshot();
        Assert.ThrowsExactly<InvalidDataException>(() => AppDataPathMapping.Apply(_root, Rules));
        EqualFiles(before);
    }

    [TestMethod]
    [DataRow("E:/Media/one")]
    [DataRow("//host/share/one")]
    public void ApplySupportsWindowsAndUncDestinations(string destination)
    {
        Execute("CREATE TABLE P(Value TEXT); INSERT INTO P VALUES('/source/one');");
        AppDataPathMapping.Apply(_root, [new("/source", destination[..^4])]);
        Assert.AreEqual(destination, Scalar("SELECT Value FROM P"));
    }

    [TestMethod]
    public void JsonKeyRenameChainsUseOnePassWithoutTemporaryDuplicateKeys()
    {
        var file = Path.Combine(_root, "app.json");
        File.WriteAllText(file, "{\"/a/file\":1,\"/b/file\":2}");
        AppDataPathMapping.Apply(_root, [new("/a", "/b"), new("/b", "/c")]);
        var result = JObject.Parse(File.ReadAllText(file));
        Assert.AreEqual(1, (int)result["/b/file"]!);
        Assert.AreEqual(2, (int)result["/c/file"]!);
    }

    [TestMethod]
    public void RuleLimitIsCheckedBeforeQuadraticCycleAnalysis()
    {
        var rules = Enumerable.Range(0, 257).Select(i => new PathMappingRule("/source/" + i, "/target/" + i)).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => AppDataPathMapping.ValidateRules(rules));
        Assert.AreEqual(256, AppDataPathMapping.ValidateRules(rules[..256]).Count);
    }

    [TestMethod]
    public void RulePrefixLengthMatchesTheSetupInputLimit()
    {
        var longPrefix = "/" + new string('a', 4096);
        Assert.ThrowsExactly<ArgumentException>(() => AppDataPathMapping.ValidateRules([new(longPrefix, "/target")]));
        Assert.ThrowsExactly<ArgumentException>(() => AppDataPathMapping.ValidateRules([new("/source", longPrefix)]));
        Assert.AreEqual(1, AppDataPathMapping.ValidateRules([new(longPrefix[..4096], "/target")]).Count);
    }

    private PathMappingScan ScanValues(params string[] values)
    {
        File.WriteAllText(Path.Combine(_root, "app.json"), JsonConvert.SerializeObject(values));
        return AppDataPathMapping.Scan(_root);
    }
    private Dictionary<string, byte[]> Snapshot() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
    private void EqualFiles(Dictionary<string, byte[]> before)
    {
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        foreach (var (file, bytes) in before) CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file));
    }
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        connection.Open(); return connection;
    }
    private void Execute(string sql, string? value = null)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = sql;
        if (value != null) command.Parameters.AddWithValue("$v", value); command.ExecuteNonQuery();
    }
    private string Scalar(string sql)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar()!.ToString()!;
    }
    private static string Join(IEnumerable<string> values, char separator) => string.Join(separator, values.Select(v => v.Replace("\\", "\\\\").Replace(separator.ToString(), "\\" + separator)));
}
