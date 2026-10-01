using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Infrastructures.Components.Configurations.App;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Semver;

namespace Bakabase.Tests;

[TestClass]
public class AutomaticBackupTests
{
    [TestMethod]
    public void ExistingConfiguration_DefaultsToEnabledAndSevenVersions()
    {
        var options = JsonConvert.DeserializeObject<AppOptions>("{\"version\":\"1.0.0\"}")!;

        Assert.IsTrue(options.EnableAutomaticBackup);
        Assert.AreEqual(7, options.MaxBackupVersions);
    }

    [TestMethod]
    public void SameVersionStartup_RetainsLatestSevenByBackupTime()
    {
        using var h = new BackupHarness();
        for (var i = 1; i <= 9; i++) h.AddBackup($"1.0.{i}", i);

        h.Run();

        CollectionAssert.AreEquivalent(Enumerable.Range(3, 7).Select(i => $"1.0.{i}").ToArray(), h.Versions);
    }

    [TestMethod]
    public void LoweringLimit_TakesEffectOnSameVersionStartup()
    {
        using var h = new BackupHarness();
        // Snapshot chronology, rather than version precedence, also handles downgrades.
        h.AddBackup("9.0.0", 1);
        h.AddBackup("3.0.0", 2);
        h.AddBackup("1.0.0", 3);

        h.Run(new AppOptions { MaxBackupVersions = 2 });

        CollectionAssert.AreEquivalent(new[] { "1.0.0", "3.0.0" }, h.Versions);
    }

    [TestMethod]
    public void Disabled_DoesNotCopyOrPruneExistingBackups()
    {
        using var h = new BackupHarness();
        h.AddBackup("1.0.0", 1);
        h.AddBackup("2.0.0", 2);
        h.WithFile("app.json", "new data");

        h.Run(new AppOptions { EnableAutomaticBackup = false, MaxBackupVersions = 1 }, "3.0.0", "4.0.0");

        CollectionAssert.AreEquivalent(new[] { "1.0.0", "2.0.0" }, h.Versions);
        Assert.IsFalse(Directory.Exists(Path.Combine(h.Backups, "3.0.0")));
    }

    [TestMethod]
    public void Disabled_DoesNotCreateBackupDirectory()
    {
        using var h = new BackupHarness();

        h.Run(new AppOptions { EnableAutomaticBackup = false }, "1.0.0", "2.0.0");

        Assert.IsFalse(Directory.Exists(h.Backups));
    }

    [TestMethod]
    public void InitialVersion_DoesNotCreateSnapshot()
    {
        using var h = new BackupHarness();
        h.WithFile("app.json", "new install");

        h.Run(previousVersion: AppConstants.InitialVersion, currentVersion: "1.0.0");

        Assert.IsFalse(Directory.Exists(h.Backups));
    }

    [TestMethod]
    public void VersionChange_CopiesRuntimeFilesAndExcludesCacheDirectoriesAndInstanceLock()
    {
        using var h = new BackupHarness();
        h.WithFile("app.json", "configuration");
        h.WithFile("database.db", "database");
        h.WithFile("configs/nested/preferences.json", "preferences");
        foreach (var directory in new[] { "temp", "components", "data" }) h.WithFile($"{directory}/ignored", "cache");
        using var instanceLock = DataDirectoryLock.TryAcquire(h.Root).Lock!;

        h.Run(previousVersion: "1.0.0", currentVersion: "2.0.0");

        var snapshot = Path.Combine(h.Backups, "1.0.0");
        Assert.AreEqual("configuration", File.ReadAllText(Path.Combine(snapshot, "app.json")));
        Assert.AreEqual("database", File.ReadAllText(Path.Combine(snapshot, "database.db")));
        Assert.AreEqual("preferences", File.ReadAllText(Path.Combine(snapshot, "configs/nested/preferences.json")));
        foreach (var directory in new[] { "backups", "temp", "components", "data" })
            Assert.IsFalse(Directory.Exists(Path.Combine(snapshot, directory)));
        Assert.IsFalse(File.Exists(Path.Combine(snapshot, DataDirectoryLock.FileName)));
        CollectionAssert.AreEquivalent(new[] { "1.0.0" }, h.Versions);
    }

    [TestMethod]
    public void Downgrade_PreservesJustCreatedSnapshotEvenWithFutureDatedBackups()
    {
        using var h = new BackupHarness();
        var future = DateTime.UtcNow.AddYears(1);
        h.AddBackup("9.0.0", future);
        h.AddBackup("8.0.0", future.AddDays(1));
        h.WithFile("database.db", "latest data");

        h.Run(new AppOptions { MaxBackupVersions = 1 }, "1.0.0", "0.9.0");

        CollectionAssert.AreEquivalent(new[] { "1.0.0" }, h.Versions);
        Assert.AreEqual("latest data", File.ReadAllText(Path.Combine(h.Backups, "1.0.0", "database.db")));
    }

    [TestMethod]
    public void ExistingSameVersionSnapshot_IsReplacedWithCompleteCurrentData()
    {
        using var h = new BackupHarness();
        h.AddBackup("1.0.0", 1);
        h.WithFile("database.db", "complete data");
        h.WithFile("configs/settings.json", "current settings");

        h.Run(previousVersion: "1.0.0", currentVersion: "2.0.0");

        var snapshot = Path.Combine(h.Backups, "1.0.0");
        Assert.AreEqual("complete data", File.ReadAllText(Path.Combine(snapshot, "database.db")));
        Assert.AreEqual("current settings", File.ReadAllText(Path.Combine(snapshot, "configs/settings.json")));
        Assert.IsFalse(File.Exists(Path.Combine(snapshot, "saved.txt")), "stale or partial contents are replaced");
        CollectionAssert.AreEquivalent(new[] { "1.0.0" }, Directory.GetDirectories(h.Backups).Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    public void FailedCopy_PreservesExistingSameVersionAndOlderBackupsWithoutPruning()
    {
        using var h = new BackupHarness();
        h.AddBackup("1.0.0", 1);
        h.AddBackup("2.0.0", 2);
        h.WithFile("one.json", "one");
        h.WithFile("two.json", "two");
        var copies = 0;

        Assert.ThrowsException<IOException>(() => h.Run(new AppOptions { MaxBackupVersions = 1 }, "2.0.0", "3.0.0",
            (source, target) =>
            {
                if (++copies == 2) throw new IOException("Simulated full disk");
                File.Copy(source, target);
            }));

        CollectionAssert.AreEquivalent(new[] { "1.0.0", "2.0.0" }, h.Versions);
        Assert.AreEqual("original 2.0.0", File.ReadAllText(Path.Combine(h.Backups, "2.0.0", "saved.txt")));
        CollectionAssert.AreEquivalent(new[] { "1.0.0", "2.0.0" }, Directory.GetDirectories(h.Backups).Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    public void FailedCopy_DoesNotPublishPartialVersionDirectory()
    {
        using var h = new BackupHarness();
        h.AddBackup("1.0.0", 1);
        h.WithFile("database.db", "database");

        Assert.ThrowsException<IOException>(() => h.Run(new AppOptions { MaxBackupVersions = 1 }, "2.0.0", "3.0.0",
            (_, _) => throw new IOException("Simulated full disk")));

        CollectionAssert.AreEquivalent(new[] { "1.0.0" }, h.Versions);
        Assert.IsFalse(Directory.Exists(Path.Combine(h.Backups, "2.0.0")));
    }

    [TestMethod]
    public void Retention_DoesNotRemoveUnrelatedFoldersOrVersionNamedFiles()
    {
        using var h = new BackupHarness();
        h.AddBackup("1.0.0", 1);
        h.AddBackup("2.0.0", 2);
        h.WithFile("backups/manual-backup/keep.txt", "manual backup");
        h.WithFile("backups/.automatic-backup-old/keep.txt", "unpublished backup");
        h.WithFile("backups/3.0.0", "a file");

        h.Run(new AppOptions { MaxBackupVersions = 1 });

        CollectionAssert.AreEquivalent(new[] { "2.0.0" }, h.Versions);
        Assert.AreEqual("manual backup", File.ReadAllText(Path.Combine(h.Backups, "manual-backup", "keep.txt")));
        Assert.AreEqual("unpublished backup", File.ReadAllText(Path.Combine(h.Backups, ".automatic-backup-old", "keep.txt")));
        Assert.AreEqual("a file", File.ReadAllText(Path.Combine(h.Backups, "3.0.0")));
    }

    [TestMethod]
    public void Retention_DoesNotFollowOrRemoveLinkedVersionDirectories()
    {
        using var h = new BackupHarness();
        h.AddBackup("1.0.0", 1);
        h.AddBackup("2.0.0", 2);
        var outside = h.CreateOutsideDirectory();
        var link = Path.Combine(h.Backups, "3.0.0");
        CreateDirectoryLinkOrSkip(link, outside);

        h.Run(new AppOptions { MaxBackupVersions = 1 });

        Assert.IsNotNull(new DirectoryInfo(link).LinkTarget);
        Assert.AreEqual("outside", File.ReadAllText(Path.Combine(outside, "keep.txt")));
        Assert.IsFalse(Directory.Exists(Path.Combine(h.Backups, "1.0.0")));
        Assert.IsTrue(Directory.Exists(Path.Combine(h.Backups, "2.0.0")));
    }

    [TestMethod]
    public void Retention_DoesNotDeleteBackupContainingNestedLink()
    {
        using var h = new BackupHarness();
        h.AddBackup("1.0.0", 1);
        h.AddBackup("2.0.0", 2);
        var outside = h.CreateOutsideDirectory();
        var link = Path.Combine(h.Backups, "1.0.0", "nested");
        CreateDirectoryLinkOrSkip(link, outside);
        Directory.SetLastWriteTimeUtc(Path.Combine(h.Backups, "1.0.0"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        h.Run(new AppOptions { MaxBackupVersions = 1 });

        Assert.IsTrue(Directory.Exists(Path.Combine(h.Backups, "1.0.0")));
        Assert.IsNotNull(new DirectoryInfo(link).LinkTarget);
        Assert.AreEqual("outside", File.ReadAllText(Path.Combine(outside, "keep.txt")));
    }

    [TestMethod]
    public void Retention_DoesNotFollowLinkedBackupRoot()
    {
        using var h = new BackupHarness();
        var outside = h.CreateOutsideDirectory();
        Directory.CreateDirectory(Path.Combine(outside, "1.0.0"));
        Directory.CreateDirectory(Path.Combine(outside, "2.0.0"));
        CreateDirectoryLinkOrSkip(h.Backups, outside);

        h.Run(new AppOptions { MaxBackupVersions = 1 });

        Assert.IsTrue(Directory.Exists(Path.Combine(outside, "1.0.0")));
        Assert.IsTrue(Directory.Exists(Path.Combine(outside, "2.0.0")));
        Assert.IsNotNull(new DirectoryInfo(h.Backups).LinkTarget);
    }

    [TestMethod]
    public void VersionChange_CopiesLinkedSourceDirectoryAsRegularSnapshotFiles()
    {
        using var h = new BackupHarness();
        var outside = h.CreateOutsideDirectory();
        CreateDirectoryLinkOrSkip(Path.Combine(h.Root, "configs"), outside);

        h.Run(previousVersion: "1.0.0", currentVersion: "2.0.0");

        var snapshotDirectory = Path.Combine(h.Backups, "1.0.0", "configs");
        Assert.IsNull(new DirectoryInfo(snapshotDirectory).LinkTarget);
        Assert.AreEqual("outside", File.ReadAllText(Path.Combine(snapshotDirectory, "keep.txt")));
    }

    [TestMethod]
    public void InvalidPersistedLimit_FallsBackToSevenVersions()
    {
        using var h = new BackupHarness();
        for (var i = 1; i <= 9; i++) h.AddBackup($"1.0.{i}", i);

        h.Run(new AppOptions { MaxBackupVersions = 0 });

        CollectionAssert.AreEquivalent(Enumerable.Range(3, 7).Select(i => $"1.0.{i}").ToArray(), h.Versions);
    }

    private static void CreateDirectoryLinkOrSkip(string path, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Inconclusive($"Symbolic links are not available on this test host: {ex.Message}");
        }
    }

    private sealed class BackupHarness : IDisposable
    {
        private readonly string _sandbox = Path.Combine(Path.GetTempPath(), $"bakabase-backup-tests-{Guid.NewGuid():N}");
        public string Root { get; }
        public string Backups => Path.Combine(Root, "backups");
        public string[] Versions => Directory.EnumerateDirectories(Backups)
            .Select(Path.GetFileName)
            .Where(name => SemVersion.TryParse(name, SemVersionStyles.Strict, out _))
            .Cast<string>().ToArray();

        public BackupHarness()
        {
            Root = Path.Combine(_sandbox, "app");
            Directory.CreateDirectory(Root);
        }

        public void WithFile(string relativePath, string contents)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        public void AddBackup(string version, int order) =>
            AddBackup(version, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(order));

        public void AddBackup(string version, DateTime time)
        {
            WithFile($"backups/{version}/saved.txt", $"original {version}");
            Directory.SetLastWriteTimeUtc(Path.Combine(Backups, version), time);
        }

        public string CreateOutsideDirectory()
        {
            var outside = Path.Combine(_sandbox, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "outside");
            return outside;
        }

        public void Run(AppOptions? options = null, string previousVersion = "5.0.0", string currentVersion = "5.0.0",
            Action<string, string>? copyFile = null) =>
            AutomaticBackup.Run(Root, SemVersion.Parse(previousVersion, SemVersionStyles.Any),
                SemVersion.Parse(currentVersion, SemVersionStyles.Any), options ?? new AppOptions(),
                NullLogger.Instance, copyFile);

        public void Dispose() => Directory.Delete(_sandbox, recursive: true);
    }
}
