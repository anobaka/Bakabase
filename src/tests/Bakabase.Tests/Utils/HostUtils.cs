using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.Logging;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Tests.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.IO;
using System.Threading.Tasks;
using System;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.Infrastructures.Components.Orm.Log;
using Bakabase.Modules.Alias.Abstractions.Services;
using Bakabase.Modules.Alias.Extensions;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Logging.LogService.Extensions;
using Bootstrap.Components.Logging.LogService.Services;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Tests.Utils;

public class HostUtils
{
    public static async Task<IServiceProvider> PrepareScopedServiceProvider()
    {
        var dbFilePath = Path.Combine(Directory.GetCurrentDirectory(), "test.db");
        File.Delete(dbFilePath);
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddLocalization();
        sc.AddBootstrapServices<InsideWorldDbContext>(c => c.UseBootstrapSqLite(Path.GetDirectoryName(dbFilePath), Path.GetFileNameWithoutExtension(dbFilePath)));
        sc.AddSingleton<LogService, SqliteLogService>();
        sc.AddBootstrapLogService<LogDbContext>(c =>
            c.UseBootstrapSqLite(Directory.GetCurrentDirectory(), "bootstrap_log"));
        sc.AddSignalR(x => { });
        sc.AddSingleton<IGuiAdapter, TestGuiAdapter>();
        sc.AddSingleton<IOptionsMonitor<FileSystemOptions>>(
            new TestOptionsMonitor<FileSystemOptions>(new FileSystemOptions()));
        sc.AddSingleton<AspNetCoreOptionsManager<FileSystemOptions>>(sp =>
            new AspNetCoreOptionsManager<FileSystemOptions>("123", "aaa",
                sp.GetRequiredService<IOptionsMonitor<FileSystemOptions>>(),
                sp.GetRequiredService<ILogger<AspNetCoreOptionsManager<FileSystemOptions>>>()));
        sc.AddSingleton<TestFileMover>();
        sc.AddAlias<InsideWorldDbContext>();
        var sp = sc.BuildServiceProvider();
        var scope = sp.CreateAsyncScope();
        var scopeSp = scope.ServiceProvider;
        var ctx = scopeSp.GetRequiredService<InsideWorldDbContext>();
        await ctx.Database.MigrateAsync();

        return scopeSp;
    }
}