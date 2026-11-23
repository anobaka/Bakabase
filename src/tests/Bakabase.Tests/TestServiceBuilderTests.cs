using Bakabase.InsideWorld.Business;
using Bakabase.TestKit.Utils;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public class TestServiceBuilderTests
{
    [TestMethod]
    public async Task Each_provider_gets_a_private_fully_migrated_WAL_database()
    {
        var first = await TestServiceBuilder.BuildServiceProvider();
        var second = await TestServiceBuilder.BuildServiceProvider();
        var firstDb = first.GetRequiredService<BakabaseDbContext>();
        var secondDb = second.GetRequiredService<BakabaseDbContext>();

        try
        {
            Assert.AreNotEqual(firstDb.Database.GetDbConnection().DataSource,
                secondDb.Database.GetDbConnection().DataSource);
            Assert.IsTrue((await firstDb.Database.GetAppliedMigrationsAsync()).Any());
            Assert.IsFalse((await firstDb.Database.GetPendingMigrationsAsync()).Any());
            Assert.IsFalse((await secondDb.Database.GetPendingMigrationsAsync()).Any());

            await secondDb.Database.OpenConnectionAsync();
            using (var command = secondDb.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode";
                Assert.AreEqual("wal", (string?)await command.ExecuteScalarAsync());
            }

            await firstDb.Database.ExecuteSqlRawAsync("CREATE TABLE TestServiceBuilderIsolation (Id INTEGER)");
            using (var command = secondDb.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'TestServiceBuilderIsolation'";
                Assert.AreEqual(0L, (long?)await command.ExecuteScalarAsync());
            }
        }
        finally
        {
            await firstDb.DisposeAsync();
            await secondDb.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task Caller_owned_database_reopens_after_the_first_provider_is_disposed()
    {
        var directory = TestServiceBuilder.NewTestDirectory();
        Assert.AreEqual(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetDirectoryName(directory));
        var connectionString = "";
        try
        {
            var first = await TestServiceBuilder.BuildServiceProvider(directory);
            await using ((IAsyncDisposable)first)
            {
                var db = first.GetRequiredService<BakabaseDbContext>();
                connectionString = db.Database.GetConnectionString()!;
                await db.Database.ExecuteSqlRawAsync("CREATE TABLE ExplicitFixture (Value INTEGER)");
                await db.Database.ExecuteSqlRawAsync("INSERT INTO ExplicitFixture VALUES (42)");
            }

            var second = await TestServiceBuilder.BuildServiceProvider(directory);
            await using ((IAsyncDisposable)second)
            {
                var db = second.GetRequiredService<BakabaseDbContext>();
                Assert.AreEqual(42, await db.Database.SqlQueryRaw<int>("SELECT Value FROM ExplicitFixture").SingleAsync());
            }
        }
        finally
        {
            if (connectionString.Length > 0)
            {
                using var connection = new SqliteConnection(connectionString);
                SqliteConnection.ClearPool(connection);
            }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

}
