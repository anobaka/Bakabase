using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.InsideWorld.Business;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bakabase.Service.Components
{
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