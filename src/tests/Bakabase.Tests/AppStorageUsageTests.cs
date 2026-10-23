using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Service.Models.View;
using Bakabase.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class AppStorageUsageTests
{
    private string _root = null!;

    [TestInitialize]
    public void Initialize() => _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "bakabase-storage-" + Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private void Write(string relative, int length)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
    }

    [TestMethod]
    public void Scan_counts_each_file_once_including_hidden_unknown_and_database_journal_files()
    {
        Write("bakabase_insideworld.db", 11);
        Write("bakabase_insideworld.db-wal", 13);
        Write("bakabase_insideworld.db-shm", 17);
        Write("bootstrap_log.db", 19);
        Write("logs/service.log", 23);
        Write("app.json", 29);
        Write("configs/property.json", 31);
        Write("data/covers/local/.hidden.jpg", 37);
        Write("data/covers-old/unknown", 41);
        Write("new-feature/payload.bin", 43);
        Write(".hidden", 47);
        Write("data/attachments/empty.txt", 0);
        // A held ownership file is measured by metadata; the scan must never open its content.
        using var ownershipLock = new FileStream(Path.Combine(_root, ".bakabase.lock"),
            FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        ownershipLock.SetLength(53);

        var result = AppStorageUsageScanner.Scan(_root, CancellationToken.None);
        Assert.AreEqual(364L, result.TotalBytes);
        Assert.AreEqual(13L, result.FileCount);
        Assert.AreEqual(result.TotalBytes, result.Categories.Sum(c => c.Bytes));
        Assert.AreEqual(result.FileCount, result.Categories.Sum(c => c.FileCount));
        Assert.AreEqual(41L, result.Categories.Single(c => c.Id == "database").Bytes);
        Assert.AreEqual(42L, result.Categories.Single(c => c.Id == "logs").Bytes);
        Assert.AreEqual(60L, result.Categories.Single(c => c.Id == "configuration").Bytes);
        Assert.AreEqual(37L, result.Categories.Single(c => c.Id == "covers").Bytes);
        Assert.AreEqual(184L, result.Categories.Single(c => c.Id == "other").Bytes);
        Assert.AreEqual(1L, result.Categories.Single(c => c.Id == "attachments").FileCount);
        Assert.IsFalse(result.Partial);
        Assert.AreEqual(0L, result.ErrorCount);
        Assert.AreEqual(0L, result.SkippedSymbolicLinks);
    }

    [TestMethod]
    public void Catalog_paths_preserve_layout_and_only_expose_dedicated_category_parents()
    {
        Assert.AreEqual(Path.Combine(_root, "acquisition", "42", "platform"),
            AppDataStorage.Acquisition.GetPath(_root, "42", "platform"));
        Assert.IsNull(AppDataStorage.GetCategoryPath(_root, "covers"), "Absent category folders must not offer an opener.");
        Directory.CreateDirectory(AppDataStorage.Covers.GetPath(_root));
        Assert.AreEqual(Path.Combine(_root, "data", "covers"), AppDataStorage.GetCategoryPath(_root, "covers"));
        foreach (var category in new[] {"database", "logs", "configuration", "downloads", "fileProcessing", "temporary", "browser", "other"})
            Assert.IsNull(AppDataStorage.GetCategoryPath(_root, category), category);
        Assert.AreEqual("other", AppDataStorage.GetCategoryId("acquisition-old/payload"));
        Assert.AreEqual("other", AppDataStorage.GetCategoryId("data/covers-extra/payload"));
        Assert.IsFalse(Directory.Exists(AppDataStorage.Acquisition.GetPath(_root)), "Resolving paths must not create them.");
        Assert.AreEqual(AppDataStorage.Locations.Select(l => l.RelativePath).Distinct().Count(), AppDataStorage.Locations.Count);
        foreach (var location in AppDataStorage.Locations)
        {
            Assert.AreEqual(location.CategoryId, AppDataStorage.GetCategoryId(location.RelativePath + "/nested/file"));
            CollectionAssert.Contains(AppDataStorage.CategoryIds.ToArray(), location.CategoryId);
        }
    }

    [TestMethod]
    public void Scan_classifies_retained_cache_cover_and_enhancer_paths_alongside_current_locations()
    {
        Write("data/cache/img/thumbnail.jpg", 11);
        Write("data/cover/legacy-cover.jpg", 13);
        Write("data/enhancer/legacy-image.jpg", 17);
        var legacy = AppStorageUsageScanner.Scan(_root, CancellationToken.None);
        Assert.AreEqual(41L, legacy.TotalBytes);
        Assert.AreEqual(11L, legacy.Categories.Single(c => c.Id == "temporary").Bytes);
        Assert.AreEqual(13L, legacy.Categories.Single(c => c.Id == "covers").Bytes);
        Assert.AreEqual(17L, legacy.Categories.Single(c => c.Id == "enhancerFiles").Bytes);
        Assert.AreEqual(AppDataStorage.LegacyCache.GetPath(_root), legacy.Categories.Single(c => c.Id == "temporary").Path);
        Assert.AreEqual(AppDataStorage.LegacyCovers.GetPath(_root), legacy.Categories.Single(c => c.Id == "covers").Path);
        Assert.AreEqual(AppDataStorage.LegacyEnhancerFiles.GetPath(_root), legacy.Categories.Single(c => c.Id == "enhancerFiles").Path);

        Write("temp/current-thumbnail.jpg", 19);
        Write("data/covers/current-cover.jpg", 23);
        Write("data/enhancers/current-image.jpg", 29);
        var combined = AppStorageUsageScanner.Scan(_root, CancellationToken.None);
        Assert.AreEqual(112L, combined.TotalBytes);
        Assert.AreEqual(6L, combined.FileCount);
        Assert.AreEqual(30L, combined.Categories.Single(c => c.Id == "temporary").Bytes);
        Assert.AreEqual(36L, combined.Categories.Single(c => c.Id == "covers").Bytes);
        Assert.AreEqual(46L, combined.Categories.Single(c => c.Id == "enhancerFiles").Bytes);
        Assert.AreEqual(0L, combined.Categories.Single(c => c.Id == "other").Bytes);
        Assert.AreEqual(combined.TotalBytes, combined.Categories.Sum(c => c.Bytes));
        foreach (var id in new[] {"temporary", "covers", "enhancerFiles"})
            Assert.IsNull(combined.Categories.Single(c => c.Id == id).Path,
                $"{id} spans current and retained directories, so it has no single dedicated parent.");
        Assert.AreEqual("other", AppDataStorage.GetCategoryId("data/cache-extra/file"));
        Assert.AreEqual("other", AppDataStorage.GetCategoryId("data/cover-old/file"));
        Assert.AreEqual("other", AppDataStorage.GetCategoryId("data/enhancer-old/file"));
    }

    [TestMethod]
    public void Scan_excludes_file_links_directory_links_cycles_and_counts_real_files_once()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Symlink creation needs special permissions on Windows.");
        var data = Directory.CreateDirectory(Path.Combine(_root, "profile")).FullName;
        Write("external/large.bin", 10000);
        Write("profile/data/covers/cover.jpg", 31);
        Write("profile/.hidden", 7);
        Directory.CreateSymbolicLink(Path.Combine(data, "outside"), Path.Combine(_root, "external"));
        Directory.CreateSymbolicLink(Path.Combine(data, "cycle"), data);
        File.CreateSymbolicLink(Path.Combine(data, "linked.bin"), Path.Combine(_root, "external", "large.bin"));
        var result = AppStorageUsageScanner.Scan(data, CancellationToken.None);
        Assert.AreEqual(38L, result.TotalBytes);
        Assert.AreEqual(2L, result.FileCount);
        Assert.AreEqual(3L, result.SkippedSymbolicLinks);
        Assert.IsFalse(result.Partial);
        // A user-selected data directory itself may be a symlink; the chosen root is followed once.
        var alias = Path.Combine(_root, "root-alias");
        Directory.CreateSymbolicLink(alias, data);
        Assert.AreEqual(38L, AppStorageUsageScanner.Scan(alias, CancellationToken.None).TotalBytes);
    }

    [TestMethod]
    public void Scan_reports_inaccessible_directories_as_partial_without_reporting_zero_as_complete()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix directory permissions exercise the real walker.");
        Write("data/covers/denied/cover.jpg", 101);
        Write("data/attachments/readable.txt", 17);
        var denied = Path.Combine(_root, "data", "covers", "denied");
        var mode = File.GetUnixFileMode(denied);
        try
        {
            File.SetUnixFileMode(denied, UnixFileMode.None);
            try
            {
                Directory.GetFileSystemEntries(denied);
                Assert.Inconclusive("The current account bypasses Unix file permissions.");
            }
            catch (UnauthorizedAccessException) { }
            var result = AppStorageUsageScanner.Scan(_root, CancellationToken.None);
            Assert.AreEqual(17L, result.TotalBytes);
            Assert.IsTrue(result.Partial);
            Assert.IsTrue(result.Categories.Single(c => c.Id == "covers").Partial);
            Assert.IsFalse(result.Categories.Single(c => c.Id == "attachments").Partial);
            Assert.AreEqual(1L, result.ErrorCount);
            Assert.AreEqual(denied, result.Errors.Single().Path);
        }
        finally { File.SetUnixFileMode(denied, mode); }
    }

    [TestMethod]
    public async Task Opening_uses_cache_refresh_replaces_it_and_failed_refresh_preserves_last_measurement()
    {
        Write("data/covers/cover.jpg", 11);
        var service = new AppStorageUsageService(() => _root, CancellationToken.None);
        var first = await service.GetAsync();
        Assert.IsFalse(first.FromCache);
        Write("data/covers/cover.jpg", 31);
        var cached = await service.GetAsync();
        Assert.AreEqual(11L, cached.TotalBytes);
        Assert.AreEqual(first.ScannedAt, cached.ScannedAt);
        Assert.IsTrue(cached.FromCache);
        var refreshed = await service.GetAsync(true);
        Assert.AreEqual(31L, refreshed.TotalBytes);
        Assert.IsFalse(refreshed.FromCache);
        Assert.AreEqual(refreshed.ScannedAt, (await service.GetAsync()).ScannedAt);
        Directory.Delete(_root, true);
        await Assert.ThrowsExceptionAsync<DirectoryNotFoundException>(() => service.GetAsync(true));
        var retained = await service.GetAsync();
        Assert.IsTrue(retained.FromCache);
        Assert.AreEqual(31L, retained.TotalBytes);
    }

    [TestMethod]
    public async Task A_different_effective_profile_cannot_receive_the_previous_profiles_cache()
    {
        Write("one/file", 7);
        Write("two/file", 13);
        var selected = Path.Combine(_root, "one");
        var service = new AppStorageUsageService(() => selected, CancellationToken.None);
        Assert.AreEqual(7L, (await service.GetAsync()).TotalBytes);
        selected = Path.Combine(_root, "two");
        var second = await service.GetAsync();
        Assert.AreEqual(13L, second.TotalBytes);
        Assert.AreEqual(selected, second.RootPath);
        Assert.IsFalse(second.FromCache);
    }

    [TestMethod]
    public async Task Concurrent_readers_and_refreshes_share_one_scan_and_reader_cancellation_does_not_cancel_it()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var service = new AppStorageUsageService(() => _root, CancellationToken.None, (root, ct) =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5), ct)) throw new TimeoutException("Test did not release scan.");
            return new AppStorageUsageViewModel {RootPath = root, TotalBytes = 7, ScannedAt = DateTimeOffset.UtcNow};
        });
        using var readerCancellation = new CancellationTokenSource();
        var first = service.GetAsync(ct: readerCancellation.Token);
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            var second = service.GetAsync();
            var refresh = service.GetAsync(true);
            readerCancellation.Cancel();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => first);
            Assert.IsFalse(second.IsCompleted);
            Assert.IsFalse(refresh.IsCompleted);
            release.Set();
            Assert.AreEqual(7L, (await second).TotalBytes);
            Assert.AreEqual(7L, (await refresh).TotalBytes);
            Assert.AreEqual(1, calls);
            Assert.IsTrue((await service.GetAsync()).FromCache);
            Assert.AreEqual(1, calls);
        }
        finally { release.Set(); }
    }

    [TestMethod]
    public async Task Missing_root_and_cancelled_scans_do_not_become_successful_cached_empty_results()
    {
        var missing = Path.Combine(_root, "missing");
        var service = new AppStorageUsageService(() => missing, CancellationToken.None);
        await Assert.ThrowsExceptionAsync<DirectoryNotFoundException>(() => service.GetAsync());
        Assert.IsFalse(Directory.Exists(missing));
        Assert.ThrowsException<OperationCanceledException>(() => AppStorageUsageScanner.Scan(_root, new CancellationToken(true)));
        var stopped = new AppStorageUsageService(() => _root, new CancellationToken(true));
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => stopped.GetAsync());
    }
}
