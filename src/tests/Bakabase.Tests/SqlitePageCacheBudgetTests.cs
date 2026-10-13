using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.InsideWorld.Business;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LogDbContext = Bakabase.Infrastructures.Components.Orm.Log.LogDbContext;
using NotificationDbContext = Bakabase.Infrastructures.Components.Orm.Notification.NotificationDbContext;
using SystemPropertyDbContext = Bakabase.Infrastructures.Components.Orm.SystemProperty.SystemPropertyDbContext;

namespace Bakabase.Tests;

// These legacy auxiliary contexts are still used by Infrastructure and must obey the same budget policy.
#pragma warning disable CS0612, CS0618

[TestClass]
public class SqlitePageCacheBudgetTests
{
    [TestMethod]
    [DataRow("main", 4096, 32 * 1024)]
    [DataRow("main", 65536, 32 * 1024)]
    [DataRow("log", 4096, 8 * 1024)]
    [DataRow("log", 65536, 8 * 1024)]
    [DataRow("notification", 4096, 8 * 1024)]
    [DataRow("notification", 65536, 8 * 1024)]
    [DataRow("system-property", 4096, 8 * 1024)]
    [DataRow("system-property", 65536, 8 * 1024)]
    public void Cache_budget_is_in_kib_regardless_of_database_page_size(string kind, int pageSize, int budgetKiB)
    {
        using var database = new DatabaseFile(pageSize);
        using var context = CreateContext(kind, database.ConnectionString);

        Assert.AreEqual(ConnectionState.Open, context.Database.GetDbConnection().State);
        Assert.AreEqual(pageSize, ReadPragma(context, "page_size"));
        Assert.AreEqual(-budgetKiB, ReadPragma(context, "cache_size"));
        using var journalCommand = context.Database.GetDbConnection().CreateCommand();
        journalCommand.CommandText = "PRAGMA journal_mode";
        Assert.AreEqual("wal", journalCommand.ExecuteScalar());
    }

    [TestMethod]
    [DataRow("main", 4096, 32 * 1024)]
    [DataRow("main", 65536, 32 * 1024)]
    [DataRow("log", 4096, 8 * 1024)]
    [DataRow("log", 65536, 8 * 1024)]
    [DataRow("notification", 4096, 8 * 1024)]
    [DataRow("notification", 65536, 8 * 1024)]
    [DataRow("system-property", 4096, 8 * 1024)]
    [DataRow("system-property", 65536, 8 * 1024)]
    public void Reused_pooled_connection_does_not_keep_a_stale_page_count_budget(string kind, int pageSize, int budgetKiB)
    {
        using var database = new DatabaseFile(pageSize);
        using (var original = CreateContext(kind, database.ConnectionString))
        {
            original.Database.ExecuteSqlRaw("PRAGMA cache_size = 5000000");
        }

        // Prove this is really a reused native connection, not a fresh connection with defaults.
        using (var pooled = new SqliteConnection(database.ConnectionString))
        {
            pooled.Open();
            using var command = pooled.CreateCommand();
            command.CommandText = "PRAGMA cache_size";
            Assert.AreEqual(5_000_000L, Convert.ToInt64(command.ExecuteScalar()));
        }

        using var reopened = CreateContext(kind, database.ConnectionString);
        Assert.AreEqual(pageSize, ReadPragma(reopened, "page_size"));
        Assert.AreEqual(-budgetKiB, ReadPragma(reopened, "cache_size"));
    }

    [TestMethod]
    [DataRow(4096)]
    [DataRow(65536)]
    public async Task Main_database_reads_and_writes_work_with_the_bounded_cache(int pageSize)
    {
        using var database = new DatabaseFile(pageSize);
        var options = new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite(database.ConnectionString).Options;
        int id;
        using (var context = new BakabaseDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
            var resource = new ResourceDbModel { Path = "/cache-budget-test/image.jpg", IsFile = true };
            context.ResourcesV2.Add(resource);
            await context.SaveChangesAsync();
            id = resource.Id;
        }

        using (var context = new BakabaseDbContext(options))
        {
            var resource = await context.ResourcesV2.SingleAsync(r => r.Id == id);
            Assert.AreEqual("/cache-budget-test/image.jpg", resource.Path);
            resource.Path = "/cache-budget-test/renamed.jpg";
            await context.SaveChangesAsync();
            Assert.AreEqual(-32 * 1024, ReadPragma(context, "cache_size"));
            Assert.AreEqual(pageSize, ReadPragma(context, "page_size"));
        }

        using (var context = new BakabaseDbContext(options))
        {
            var resource = await context.ResourcesV2.SingleAsync(r => r.Id == id);
            Assert.AreEqual("/cache-budget-test/renamed.jpg", resource.Path);
            context.ResourcesV2.Remove(resource);
            await context.SaveChangesAsync();
            Assert.IsFalse(await context.ResourcesV2.AnyAsync());
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Invalid_budget_is_rejected_before_opening_a_connection(int budgetKiB)
    {
        using var context = new DbContext(new DbContextOptionsBuilder().UseSqlite("Data Source=:memory:").Options);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.SetSqlitePageCacheBudget(budgetKiB));
        Assert.AreEqual(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [TestMethod]
    public async Task Fresh_startup_preserves_the_effective_budget_after_initializing_64k_pages()
    {
        using var database = new DatabaseFile(null);
        using var services = new ServiceCollection()
            .AddDbContext<MigrationBudgetDbContext>(options => options.UseSqlite(database.ConnectionString))
            .BuildServiceProvider();

        await services.MigrateSqliteDbContexts<MigrationBudgetDbContext>();

        // Keep the native connection from startup: a new one would hide a rescaled cache.
        using var connection = new SqliteConnection(database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA page_size";
        Assert.AreEqual(65536L, Convert.ToInt64(command.ExecuteScalar()));
        command.CommandText = "PRAGMA cache_size";
        Assert.AreEqual(-1024L, Convert.ToInt64(command.ExecuteScalar()));
        command.CommandText = """
            CREATE TABLE CachePayload (Id INTEGER PRIMARY KEY, Payload BLOB);
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<4096)
            INSERT INTO CachePayload SELECT i, zeroblob(1024) FROM n;
            SELECT sum(length(Payload)) FROM CachePayload;
            """;
        command.ExecuteNonQuery();

        var result = SQLitePCL.raw.sqlite3_db_status(connection.Handle,
            SQLitePCL.raw.SQLITE_DBSTATUS_CACHE_USED, out var cacheBytes, out _, 0);
        Assert.AreEqual(0, result);
        Assert.IsTrue(cacheBytes <= 1024 * 1024 + 256 * 1024,
            $"The 1 MiB budget grew to {cacheBytes} bytes after startup changed the page size.");
    }

    public sealed class MigrationBudgetDbContext : DbContext
    {
        public MigrationBudgetDbContext(DbContextOptions<MigrationBudgetDbContext> options) : base(options) =>
            this.SetSqlitePageCacheBudget(1024);
    }

    private static DbContext CreateContext(string kind, string connectionString) => kind switch
    {
        "main" => new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite(connectionString).Options),
        "log" => new LogDbContext(new DbContextOptionsBuilder<LogDbContext>().UseSqlite(connectionString).Options),
        "notification" => new NotificationDbContext(new DbContextOptionsBuilder<NotificationDbContext>().UseSqlite(connectionString).Options),
        "system-property" => new SystemPropertyDbContext(new DbContextOptionsBuilder<SystemPropertyDbContext>().UseSqlite(connectionString).Options),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static int ReadPragma(DbContext context, string name)
    {
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA " + name;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private sealed class DatabaseFile : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "bakabase-sqlite-budget-" + Guid.NewGuid().ToString("N"));
        public string ConnectionString { get; }

        public DatabaseFile(int? pageSize)
        {
            Directory.CreateDirectory(_directory);
            ConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_directory, "budget.db")
            }.ToString();

            if (!pageSize.HasValue) return;

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA page_size = {pageSize}; VACUUM; PRAGMA journal_mode = WAL;";
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            // Clear only this test's unique pool, never pools belonging to another test or app.
            using var connection = new SqliteConnection(ConnectionString);
            SqliteConnection.ClearPool(connection);
            Directory.Delete(_directory, true);
        }
    }
}

#pragma warning restore CS0612, CS0618
