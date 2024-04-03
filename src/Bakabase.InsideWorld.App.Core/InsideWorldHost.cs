using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Infrastructures.Resources;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Caching;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Configurations.Models.Db;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using InsideWorld.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.App.Core
{
    public class InsideWorldHost : AppHost
    {
        public InsideWorldHost(IGuiAdapter guiAdapter, ISystemService systemService) : base(guiAdapter, systemService)
        {
        }

        protected override int ListeningPortCount => 3;

        protected override Assembly[] AssembliesForGlobalConfigurationRegistrationsScanning =>
            new[] {Assembly.GetAssembly(SpecificTypeUtils<ResourceOptions>.Type)!};

        protected override void Initialize()
        {
            base.Initialize();

            // Move previous app data
            // This is a temporary hard code, and will be removed after 1.7.x.
            V170Migrator.CopyCoreAppData();
        }

        protected override IHostBuilder CreateHostBuilder(params string[] args) =>
            AppUtils.CreateAppHostBuilder<InsideWorldStartup>(args);

        protected override string DisplayName => "Inside World";

        protected override async Task MigrateDb(IServiceProvider serviceProvider)
        {
            await serviceProvider.MigrateSqliteDbContexts<InsideWorldDbContext>();
            await base.MigrateDb(serviceProvider);
        }

        protected override async Task ExecuteCustomProgress(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var textService = scope.ServiceProvider.GetRequiredService<SpecialTextService>();

            var bcc = scope.ServiceProvider.GetRequiredService<GlobalCacheContainer>();
            bcc.SpecialTextVersion = await textService.CalcVersion();

            var updaterOptionsManager =
                scope.ServiceProvider.GetRequiredService<IBOptionsManager<UpdaterOptions>>();
            // force fixing oss configuration
            await updaterOptionsManager.SaveAsync(a =>
                a.AppUpdaterOssObjectPrefix = BusinessConstants.AppOssObjectPrefix);

            var dependencies = serviceProvider.GetRequiredService<IEnumerable<IDependentComponentService>>().ToList();
            foreach (var d in dependencies)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Logger.LogInformation($"Trying to discover dependency [{d.DisplayName}({d.Id})]");
                        await d.Discover(new CancellationToken());
                    }
                    catch (Exception e)
                    {
                        Logger.LogError(e, $"Failed to discover dependency [{d.DisplayName}({d.Id})]: {e.Message}");
                    }

                    if (d is {IsRequired: true, Status: DependentComponentStatus.NotInstalled})
                    {
                        Logger.LogInformation($"Dependency [{d.DisplayName}({d.Id})] is not installed, installing...");
                        await d.Install(new CancellationToken());
                    }
                });
            }
        }

        protected override Task<string?> CheckIfAppCanExitSafely()
        {
            var taskManager = Host.Services.GetRequiredService<BackgroundTaskManager>();
            var localizer = Host.Services.GetRequiredService<AppLocalizer>();
            var tasks = taskManager?.Tasks;
            return Task.FromResult(tasks?.Any(t =>
                t.Status == BackgroundTaskStatus.Running &&
                t.Level == BackgroundTaskLevel.Critical) == true
                ? localizer.App_CriticalTasksRunningOnExit()
                : null);
        }
    }
}