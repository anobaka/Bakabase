using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Infrastructures.Resources;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Service.Components.Tasks;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components
{
    public class BakabaseHost(IGuiAdapter guiAdapter, ISystemService systemService) : AppHost(guiAdapter, systemService)
    {
        protected override string? SingleInstanceId => "Bakabase";

        protected override int DefaultAutoListeningPortCount => 3;

        protected override Assembly[] AssembliesForGlobalConfigurationRegistrationsScanning =>
            [
                Assembly.GetAssembly(SpecificTypeUtils<ResourceOptions>.Type)!,
                Assembly.GetAssembly(SpecificTypeUtils<UIOptions>.Type)!,
                Assembly.GetAssembly(SpecificTypeUtils<TaskOptions>.Type)!,
            ];

        protected override string OverrideFeAddress(string feAddress)
        {
            try
            {
                var startupPage = Host.Services.GetRequiredService<IBOptions<UIOptions>>().Value?.StartupPage;
                if (startupPage.HasValue)
                {
                    switch (startupPage.Value)
                    {
                        case StartupPage.Default:
                            break;
                        case StartupPage.Resource:
                        {
                            var hashIndex = feAddress.IndexOf('#');
                            if (hashIndex > -1)
                            {
                                feAddress = feAddress.Substring(0, hashIndex);
                            }

                            feAddress = feAddress.TrimEnd('/') + "/#/resource";
                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
            catch (Exception)
            {
                // ignored
            }

            return feAddress;
        }

        protected override void Initialize()
        {
            Env.Load();
            base.Initialize();
        }

        protected override IHostBuilder CreateHostBuilder(params string[] args) =>
            AppUtils.CreateAppHostBuilder<BakabaseStartup>(args);

        protected override string DisplayName => "Bakabase";

        protected override async Task MigrateDb(IServiceProvider serviceProvider)
        {
            await serviceProvider.MigrateSqliteDbContexts<BakabaseDbContext>();
            await base.MigrateDb(serviceProvider);
        }

        protected override async Task ExecuteCustomProgress(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();

            // Clean up logs older than 30 days
            var logService = serviceProvider.GetRequiredService<Bootstrap.Components.Logging.LogService.Services.LogService>();
            await logService.DeleteBefore(DateTime.Now.AddDays(-7));

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

            var dynamicTaskRegistry = serviceProvider.GetRequiredService<DynamicTaskRegistry>();
            var taskManager = serviceProvider.GetRequiredService<BTaskManager>();

            // Initialize BTaskManager
            await taskManager.Initialize();

            // Register all predefined tasks via DI discovery
            await dynamicTaskRegistry.RegisterAllTasksAsync();
        }

        protected override Task<string?> CheckIfAppCanExitSafely()
        {
            var taskManager = Host.Services.GetRequiredService<BTaskManager>();
            var localizer = Host.Services.GetRequiredService<AppLocalizer>();
            var tasks = taskManager?.GetTasksViewModel();
            return Task.FromResult(tasks?.Any(t =>
                t is {Status: BTaskStatus.Running, Level: BTaskLevel.Critical}) == true
                ? localizer.App_CriticalTasksRunningOnExit()
                : null);
        }
    }
}