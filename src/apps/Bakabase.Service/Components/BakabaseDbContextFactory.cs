using Bakabase.InsideWorld.Business;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bakabase.Service.Components
{
    /// <summary>
    /// Design-time factory used by <c>dotnet ef migrations</c> only. At design time the
    /// effective AppData dir is unknown. Model generation must never open a user's library:
    /// the context constructor opens its connection to apply the page-cache budget.
    /// An in-memory connection provides the same SQLite model without filesystem side effects.
    /// </summary>
    public class BakabaseDbContextFactory : IDesignTimeDbContextFactory<BakabaseDbContext>
    {
        public BakabaseDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<BakabaseDbContext>();
            optionsBuilder.UseSqlite("Data Source=:memory:");
            return new BakabaseDbContext(optionsBuilder.Options);
        }
    }
}