using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.InsideWorld.Business;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bakabase.Service.Components
{
    /// <summary>
    /// Design-time factory used by <c>dotnet ef migrations</c> only. At design time the
    /// effective AppData dir is unknown (no DI, no app.json, no anchor redirect), so we
    /// use the platform-default anchor — the actual runtime DB lives under the user's
    /// effective <c>AppDataDirectory</c>.
    /// </summary>
    public class BakabaseDbContextFactory : IDesignTimeDbContextFactory<BakabaseDbContext>
    {
        public BakabaseDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<BakabaseDbContext>();
            var appDataPath = AppService.DefaultAppDataDirectory;
            optionsBuilder.UseBootstrapSqLite(appDataPath, "bakabase_insideworld");
            return new BakabaseDbContext(optionsBuilder.Options);
        }
    }
}