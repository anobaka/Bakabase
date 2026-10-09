using Bakabase.Abstractions.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Net;
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
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.HealthScore.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Service.Components.Tasks;
using Bakabase.Service.Components.ServerData;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components
{
    public class BakabaseHost(IGuiAdapter guiAdapter, ISystemService systemService,
        Func<CancellationToken, Task>? beforeServerStart = null) : AppHost(guiAdapter, systemService)
    {
        protected override string? SingleInstanceId => "Bakabase";

        protected override int DefaultAutoListeningPortCount => 3;

        protected override IReadOnlyList<int>? OverrideListeningPorts() => guiAdapter is NullGuiAdapter
            ? ParseServerListeningPorts(Environment.GetEnvironmentVariable("API_LISTENING_PORTS"))
            : base.OverrideListeningPorts();

        internal static IReadOnlyList<int> ParseServerListeningPorts(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return [34567];
            var parts = value.Split([',', ';'], StringSplitOptions.TrimEntries);
            if (parts.Any(p => !int.TryParse(p, out var port) || port is < 1 or > 65535))
                throw new ArgumentException("API_LISTENING_PORTS must contain ports from 1 to 65535 separated by commas or semicolons.");
            return parts.Select(int.Parse).Distinct().ToArray();
        }

        protected override string ListeningInterface
        {
            get
            {
                if (guiAdapter is not NullGuiAdapter) return base.ListeningInterface;
                return ServerListeningInterface();
            }
        }

        internal static string ServerListeningInterface()
        {
            var configured = Environment.GetEnvironmentVariable("BAKABASE_BIND_ADDRESS");
            if (string.IsNullOrWhiteSpace(configured))
                return Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ? "0.0.0.0" : "127.0.0.1";
            if (!IPAddress.TryParse(configured, out var address))
                throw new ArgumentException("BAKABASE_BIND_ADDRESS must be an IP address.");
            return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();
        }

        protected override Assembly[] AssembliesForGlobalConfigurationRegistrationsScanning =>
            [
                Assembly.GetAssembly(SpecificTypeUtils<ResourceOptions>.Type)!,
                Assembly.GetAssembly(SpecificTypeUtils<UIOptions>.Type)!,
                Assembly.GetAssembly(SpecificTypeUtils<TaskOptions>.Type)!,
                typeof(AcquisitionOptions).Assembly,
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

        protected override IHostBuilder CreateHostBuilder(params string[] args)
        {
            var builder = AppUtils.CreateAppHostBuilder<BakabaseStartup>(args);
            Func<CancellationToken, Task>? handoff = SetupChildConnection.Current is { Role: "business" } child
                ? child.BeforeListenAsync : beforeServerStart;
            if (handoff != null)
                builder.ConfigureServices(services => services.AddSingleton<IHostedService>(new ImportProgressHandoff(handoff)));
            return builder;
        }

        protected override string DisplayName => "Bakabase";

        protected override async Task MigrateDb(IServiceProvider serviceProvider)
        {
            // Eagerly resolve so AppDataPaths.Relocator is installed (via the relocator's
            // ctor) before any DB ↔ domain extension method runs during data migrations.
            _ = serviceProvider.GetRequiredService<Bakabase.Abstractions.Components.FileSystem.IAppDataPathRelocator>();

            await serviceProvider.MigrateSqliteDbContexts<BakabaseDbContext>();
            await base.MigrateDb(serviceProvider);
        }

        protected override async Task ExecuteCustomProgress(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();

            // Clean up logs older than 30 days
            var logService = serviceProvider.GetRequiredService<Bootstrap.Components.Logging.LogService.Services.LogService>();
            await logService.DeleteBefore(DateTime.Now.AddDays(-7));

            // Discover already-installed tools in the background so the UI reports their real
            // status. Discovery only probes local paths; installing still waits for a consumer,
            // so starting an empty library to browse other devices never downloads anything.
            foreach (var d in serviceProvider.GetRequiredService<IEnumerable<IDependentComponentService>>())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await d.Discover(CancellationToken.None);
                    }
                    catch (Exception e)
                    {
                        Logger.LogError(e, $"Failed to discover dependency [{d.DisplayName}({d.Id})]: {e.Message}");
                    }
                });
            }

            // Builtin text types are defined in code, so their rows are an invariant. Creating
            // them here — post-migration, before anything serves a request — keeps reads pure and
            // the management page complete regardless of what seeding history a database has.
            await scope.ServiceProvider.GetRequiredService<ITextVocabularyService>().EnsureBuiltinTypes();

            // Warm in-memory caches that depend on tables created by migrations.
            // Must run here (post-migration) — not via IHostedService, which fires
            // during host start before MigrateDb has executed.
            await serviceProvider.GetRequiredService<IHealthScoreCacheWarmer>().WarmAsync();

            // Resolved so it exists and has subscribed; it has no other job.
            serviceProvider.GetRequiredService<Components.Collections.CollectionRuleIndexInvalidator>();

            var dynamicTaskRegistry = serviceProvider.GetRequiredService<DynamicTaskRegistry>();
            var taskManager = serviceProvider.GetRequiredService<BTaskManager>();

            // Rebuild durable move reservations before any synchronization task can start.
            await using (var resourceMoveScope = serviceProvider.CreateAsyncScope())
            {
                await resourceMoveScope.ServiceProvider
                    .GetRequiredService<Bakabase.Abstractions.Services.IResourceMoveService>()
                    .MarkInterruptedOnStartup();
            }

            // Initialize BTaskManager
            await taskManager.Initialize();

            // Register all predefined tasks via DI discovery
            await dynamicTaskRegistry.RegisterAllTasksAsync();

            // Workflow runs survive process restart through the DB. Normalize
            // crash-interrupted ones, then re-enqueue any pending rows so events that
            // arrived right before shutdown don't get dropped on the floor.
            await using (var workflowScope = serviceProvider.CreateAsyncScope())
            {
                var rehydrator = workflowScope.ServiceProvider
                    .GetRequiredService<Bakabase.Modules.Workflow.Components.WorkflowRunRehydrator<BakabaseDbContext>>();
                await rehydrator.MarkInterruptedRunsAsync();
                await rehydrator.ReEnqueuePendingRunsAsync();
            }

            // The built-in acquisition recipes are seeded by name, once each. A recipe whose steps
            // this build does not have yet is skipped and seeded by a later release instead.
            await using (var acquisitionScope = serviceProvider.CreateAsyncScope())
            {
                await acquisitionScope.ServiceProvider
                    .GetRequiredService<Bakabase.Modules.Acquisition.Components.AcquisitionRecipeSeeder<
                        BakabaseDbContext>>()
                    .SeedAsync();
                await acquisitionScope.ServiceProvider
                    .GetRequiredService<Components.FileProcessing.FileProcessingWorkflowSeeder>()
                    .SeedAsync();
                await acquisitionScope.ServiceProvider
                    .GetRequiredService<Components.Downloader.DownloadResultWorkflowService>()
                    .SeedAsync();
                await acquisitionScope.ServiceProvider
                    .GetRequiredService<Bakabase.InsideWorld.Business.Components.PostParser.Workflow.PostParserWorkflowService<BakabaseDbContext>>()
                    .SeedAsync();
            }

        }

        protected override Task<string?> CheckIfAppCanExitSafely()
        {
            var taskManager = Host.Services.GetRequiredService<BTaskManager>();
            var localizer = Host.Services.GetRequiredService<AppLocalizer>();
            var tasks = taskManager?.GetTasksViewModel();
            return Task.FromResult(tasks?.Any(t =>
                t.Level == BTaskLevel.Critical && t.Status.IsActive()) == true
                ? localizer.App_CriticalTasksRunningOnExit()
                : null);
        }
    }
}
