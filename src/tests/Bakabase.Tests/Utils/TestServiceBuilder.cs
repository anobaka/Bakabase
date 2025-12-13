using System;
using System.IO;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.App.Upgrade.Adapters;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.Logging;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.Infrastructures.Components.Orm.Log;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bootstrap.Components.Configuration.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BakabaseUpdater;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip;
using Bakabase.InsideWorld.Business.Components.FileMover;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;
using Bakabase.Modules.ThirdParty.Extensions;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;
using Bakabase.Service.Extensions;
using Bakabase.Tests.Implementations;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Logging.LogService.Extensions;
using Bootstrap.Components.Logging.LogService.Services;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bakabase.Tests.Utils;

/// <summary>
/// Test service builder that mirrors BakabaseStartup configuration
/// but replaces GUI and other non-testable components with test implementations.
/// </summary>
public static class TestServiceBuilder
{
    public static async Task<IServiceProvider> BuildServiceProvider()
    {
        // Use unique database file names to avoid conflicts between parallel tests
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var testDir = Path.Combine(Path.GetTempPath(), $"BakabaseTests_{uniqueId}");
        Directory.CreateDirectory(testDir);

        var dbFilePath = Path.Combine(testDir, "test.db");

        var services = new ServiceCollection();

        // === Basic Services ===
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddLocalization();
        services.AddSignalR(x => { });

        // === Database ===
        services.AddBootstrapServices<BakabaseDbContext>(c =>
            c.UseBootstrapSqLite(testDir, "test"));
        services.AddSingleton<LogService, SqliteLogService>();
        services.AddBootstrapLogService<LogDbContext>(c =>
            c.UseBootstrapSqLite(testDir, "bootstrap_log"));

        // === Core Business Services (from BakabaseStartup) ===
        services.AddInsideWorldBusinesses();

        // === HTTP Client ===
        services.AddBakabaseHttpClient<BakabaseHttpClientHandler>(InternalOptions.HttpClientNames.Default);

        // === Cookie Validators ===
        services.TryAddSingleton<BilibiliCookieValidator>();
        services.TryAddSingleton<ExHentaiCookieValidator>();
        services.TryAddSingleton<PixivCookieValidator>();
        services.RegisterAllRegisteredTypeAs<ICookieValidator>();

        // === Options Manager Pool ===
        services.AddSingleton<BakabaseOptionsManagerPool>();

        // === Third Party Logger ===
        services.AddSingleton<ThirdPartyHttpRequestLogger>();

        // === Dependency Services ===
        services.TryAddSingleton<FfMpegService>();
        services.TryAddSingleton<HardwareAccelerationService>();
        services.TryAddSingleton<LuxService>();
        services.TryAddSingleton<BakabaseUpdaterService>();
        services.TryAddSingleton<SevenZipService>();
        services.RegisterAllRegisteredTypeAs<IDependentComponentService>();
        services.TryAddSingleton<IBakabaseUpdater>(sp => sp.GetRequiredService<BakabaseUpdaterService>());

        // === Compression ===
        services.TryAddSingleton<CompressedFileService>();

        // === File Mover ===
        services.TryAddSingleton<IFileMover, FileMover>();
        services.AddSingleton<TestFileMover>();

        // === Web Proxy ===
        services.TryAddSingleton<BakabaseWebProxy>();

        // === MediaLibrary Template (includes PathMark services) ===
        services.AddMediaLibraryTemplate<BakabaseDbContext>();

        // === BTask ===
        services.AddBTask<TestBTaskEventHandler>();

        // ==========================
        // TEST IMPLEMENTATIONS
        // ==========================

        // GUI Adapter - use test implementation
        services.AddSingleton<IGuiAdapter, TestGuiAdapter>();

        // Localizer - use test implementation
        services.AddTransient<IBakabaseLocalizer, TestBakabaseLocalizer>();
        services.AddTransient<IDependencyLocalizer, TestDependencyLocalizer>();

        // System Service - use test implementation
        services.AddSingleton<ISystemService, TestSystemService>();

        // Cover Discoverer - use test implementation
        services.AddSingleton<ICoverDiscoverer, TestCoverDiscoverer>();

        // System Player - use test implementation
        services.AddSingleton<ISystemPlayer, TestSystemPlayer>();

        // PrepareCache Trigger - use test implementation
        services.AddSingleton<IPrepareCacheTrigger, TestPrepareCacheTrigger>();

        // File Manager
        services.AddSingleton<IFileManager, FileManager>();

        // FileSystem Options
        services.AddSingleton<IOptionsMonitor<FileSystemOptions>>(
            new TestOptionsMonitor<FileSystemOptions>(new FileSystemOptions()));
        services.AddSingleton<AspNetCoreOptionsManager<FileSystemOptions>>(sp =>
            new AspNetCoreOptionsManager<FileSystemOptions>("filesystem", "filesystem",
                sp.GetRequiredService<IOptionsMonitor<FileSystemOptions>>(),
                sp.GetRequiredService<ILogger<AspNetCoreOptionsManager<FileSystemOptions>>>()));

        // Task Options
        services.AddSingleton<IOptionsMonitor<TaskOptions>>(
            new TestOptionsMonitor<TaskOptions>(new TaskOptions()));
        services.AddSingleton<AspNetCoreOptionsManager<TaskOptions>>(sp =>
            new AspNetCoreOptionsManager<TaskOptions>("task", "task",
                sp.GetRequiredService<IOptionsMonitor<TaskOptions>>(),
                sp.GetRequiredService<ILogger<AspNetCoreOptionsManager<TaskOptions>>>()));

        // Resource Options
        services.AddSingleton<IBOptions<ResourceOptions>>(
            new TestBOptions<ResourceOptions>(new ResourceOptions()));
        services.AddSingleton<IBOptionsManager<ResourceOptions>>(sp =>
            new TestBOptionsManager<ResourceOptions>(sp.GetRequiredService<IBOptions<ResourceOptions>>().Value));

        // App Options
        services.AddSingleton<IBOptionsManager<AppOptions>>(
            new TestBOptionsManager<AppOptions>(new AppOptions()));

        // Build provider and initialize database
        var sp = services.BuildServiceProvider();
        var scope = sp.CreateAsyncScope();
        var scopeSp = scope.ServiceProvider;

        var ctx = scopeSp.GetRequiredService<BakabaseDbContext>();
        await ctx.Database.MigrateAsync();

        return scopeSp;
    }
}
