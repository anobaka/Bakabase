using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Models.Db;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.Apply.DataSyncApplyFixture;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// §8.10.4 (Q11): a resolution or undo that would destroy data — delete an entity with values, delete a held child
/// that resources use, convert a type lossily, or convert one back — copies the database with <c>VACUUM INTO</c>
/// first, before its transaction, into the data sync backups folder (§4.7); the newest five are kept; a copy that
/// fails ends the task <c>BackupFailed</c> with nothing applied.
/// </summary>
[TestClass]
public class BackupTests
{
    private DataSyncApplyFixture _f = null!;
    private DataSyncPeer _peer = null!;
    private DataSyncLinkDbModel _link = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _f = await CreateAsync();
        _peer = new DataSyncPeer("PC-1");
        _link = await _f.LinkAsync(_peer);
    }

    private string[] Backups() =>
        Directory.Exists(_f.Directory.BackupsPath)
            ? Directory.GetFiles(_f.Directory.BackupsPath, DataSyncRetention.BackupSearchPattern)
            : [];

    /// <summary>A definition with values made here, deleted by the peer: DeleteHere is destructive.</summary>
    private async Task<(string Key, string LocalKey, DataSyncInboxItemDbModel Item)> DeletedThereWithValuesAsync()
    {
        var localKey = _f.Kind.Add(Content("Mood", ("m", "Calm")));
        _f.Kind.Values[localKey] = 3;
        await _f.RefreshAsync();
        var row = await _f.RowAsync(localKey);
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Tombstone([row.SyncKey], _peer.Next(Vv(row.VvJson)))));
        return (row.SyncKey, localKey, (await _f.OpenItemsAsync()).Single());
    }

    [TestMethod]
    public async Task A_destructive_resolution_copies_the_database_before_its_transaction()
    {
        var (key, localKey, item) = await DeletedThereWithValuesAsync();

        await _f.ResolveAsync(item, DataSyncInboxAction.DeleteHere, backup: true);

        Assert.IsFalse(_f.Kind.Definitions.ContainsKey(localKey), "applied");
        var backup = Backups().Single();
        StringAssert.Matches(Path.GetFileName(backup), new System.Text.RegularExpressions.Regex(@"^data-sync-\d{8}-\d{6}\.db$"));
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = backup, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM DataSyncEntities WHERE SyncKey = $k AND DeletedAtUtc IS NULL";
        command.Parameters.AddWithValue("$k", key);
        Assert.AreEqual(1L, await command.ExecuteScalarAsync(), "the copy holds the state before the apply");
    }

    [TestMethod]
    public async Task A_batch_that_destroys_nothing_takes_no_backup_and_none_is_taken_unless_asked()
    {
        var (_, _, item) = await DeletedThereWithValuesAsync();
        await _f.ResolveAsync(item, DataSyncInboxAction.KeepHereOnly, backup: true);
        Assert.AreEqual(0, Backups().Length);

        var (_, _, second) = await DeletedThereWithValuesAsync();
        await _f.ResolveAsync(second, DataSyncInboxAction.DeleteHere, backup: false);
        Assert.AreEqual(0, Backups().Length, "the person may go on without the backup");
    }

    [TestMethod]
    public async Task Retention_keeps_the_newest_five()
    {
        Directory.CreateDirectory(_f.Directory.BackupsPath);
        for (var i = 0; i < 6; i++)
            File.WriteAllText(Path.Combine(_f.Directory.BackupsPath, $"data-sync-2020010{i}-000000.db"), "old");
        File.WriteAllText(Path.Combine(_f.Directory.BackupsPath, "app-backup.zip"), "not ours");
        var (_, _, item) = await DeletedThereWithValuesAsync();

        await _f.ResolveAsync(item, DataSyncInboxAction.DeleteHere, backup: true);

        var kept = Backups().Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.AreEqual(5, kept.Count);
        Assert.IsFalse(kept.Contains("data-sync-20200100-000000.db"), "the oldest went");
        Assert.IsTrue(kept.Last()!.StartsWith("data-sync-2026", StringComparison.Ordinal), "the new copy stays");
        Assert.IsTrue(File.Exists(Path.Combine(_f.Directory.BackupsPath, "app-backup.zip")), "other files are never touched");
    }

    [TestMethod]
    public async Task Backups_taken_in_the_same_second_sort_by_time_and_retention_keeps_the_newest()
    {
        var backup = _f.Services.GetRequiredService<DataSyncBackup>();
        var copies = new List<string>();
        // One clock second (the fixture's clock does not move): each copy after the first carries a counter.
        for (var i = 0; i < 12; i++) copies.Add(await backup.CreateAsync(_f.NewDb(), default));

        Assert.AreEqual(12, copies.Distinct().Count());
        var kept = copies.TakeLast(DataSyncRetention.BackupsKept).ToList();
        CollectionAssert.AreEquivalent(kept, Backups().ToList(), "every copy pruned the oldest, never a newer one");
        CollectionAssert.AreEqual(kept.Select(Path.GetFileName).Reverse().ToList(),
            Backups().Select(Path.GetFileName).OrderByDescending(n => n, StringComparer.Ordinal).ToList(),
            "newest first by name, as retention reads them");
    }

    [TestMethod]
    public async Task A_failed_backup_ends_the_task_BackupFailed_with_nothing_applied()
    {
        var (key, localKey, item) = await DeletedThereWithValuesAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(_f.Directory.BackupsPath)!);
        File.WriteAllText(_f.Directory.BackupsPath, "a file where the folder should be");

        var failure = await Assert.ThrowsExceptionAsync<BTaskException>(() =>
            _f.ResolveAsync(item, DataSyncInboxAction.DeleteHere, backup: true));

        Assert.AreEqual(DataSyncBackupFailedException.Code, failure.BriefMessage);
        Assert.IsTrue(_f.Kind.Definitions.ContainsKey(localKey), "nothing applied");
        Assert.IsNull((await _f.ByKeyAsync(key))!.DeletedAtUtc);
        Assert.IsNull((await _f.ItemsAsync()).Single(i => i.Id == item.Id).ClosedAtUtc);
    }

    [TestMethod]
    public async Task A_lossy_conversion_and_the_undo_that_converts_it_back_are_both_backed_up()
    {
        var key = SyncKey.New().Value;
        var v1 = _peer.Next();
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], v1, Content("Genre", ("a", "Rock")).With(type: "Multiple"), "a0")));
        var localKey = _f.Kind.KeyOf("Genre");
        _f.Kind.Values[localKey] = 10;
        _f.Kind.Lossy[localKey] = 4;
        await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([key], _peer.Next(v1), Content("Genre", ("a", "Rock")).With(type: "Single"), "a0")));

        var logId = await _f.ResolveAsync((await _f.OpenItemsAsync()).Single(), DataSyncInboxAction.Convert, backup: true);
        Assert.AreEqual(1, Backups().Length, "the conversion is lossy");

        _f.Clock.Advance(TimeSpan.FromSeconds(2));
        await _f.UndoAsync(logId!.Value);
        Assert.AreEqual(2, Backups().Length, "converting back is lossy too (product must-fix 18)");
        Assert.AreEqual("Multiple", _f.Kind[localKey].Type);
    }
}
