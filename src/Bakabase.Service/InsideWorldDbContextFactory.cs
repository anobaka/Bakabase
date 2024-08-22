using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.InsideWorld.Business;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bakabase.Service
{
    public class InsideWorldDbContextFactory : IDesignTimeDbContextFactory<InsideWorldDbContext>
    {
        public InsideWorldDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<InsideWorldDbContext>();
            var appDataPath = AppService.DefaultAppDataDirectory;
            optionsBuilder.UseBootstrapSqLite(appDataPath, "bakabase_insideworld");
            return new InsideWorldDbContext(optionsBuilder.Options);
        }
    }
}