using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.Infrastructures.Components.Orm.Log;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bakabase.Service.Components
{
    public class LogDbContextFactory : IDesignTimeDbContextFactory<LogDbContext>
    {
        public LogDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<LogDbContext>();
            var appDataPath = AppService.DefaultAppDataDirectory;
            optionsBuilder.UseBootstrapSqLite(appDataPath, "bootstrap_log");
            return new LogDbContext(optionsBuilder.Options);
        }
    }
}
