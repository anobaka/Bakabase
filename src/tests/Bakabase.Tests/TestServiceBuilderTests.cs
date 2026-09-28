using Bakabase.InsideWorld.Business;
using Bakabase.TestKit.Utils;
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
}
