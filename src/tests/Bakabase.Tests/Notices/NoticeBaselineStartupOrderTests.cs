using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Service.Components.Notices;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Notices;

/// <summary>
/// The fresh-install rule inside a real host start: <see cref="NoticeBaselineInitializer"/>
/// has to learn whether this is an install's first start before the host records the running
/// version over the one the install last ran — whichever of a running host's hooks records it.
/// </summary>
/// <remarks>
/// The Microsoft.Extensions.Hosting host, started and stopped as the app's host starts and
/// stops it; the version is recorded the way <c>AppHost</c> records it (from a task started by
/// <see cref="IHostApplicationLifetime.ApplicationStarted"/>, after migrations) or from a
/// hosted service on either side of the initializer, which is where that write would most
/// likely move. <c>AppHost</c> itself lives in the Infrastructures submodule and owns the
/// process (its data directory, ports and statics), so it is not started here.
/// </remarks>
[TestClass]
public sealed class NoticeBaselineStartupOrderTests
{
    private const string RunningVersion = "2.4.0";

    public enum Recorder
    {
        ApplicationStarted,
        HostedServiceBefore,
        HostedServiceAfter
    }

    [TestMethod]
    [DataRow(Recorder.ApplicationStarted, DisplayName = "Version recorded from ApplicationStarted, as AppHost does")]
    [DataRow(Recorder.HostedServiceBefore, DisplayName = "Version recorded by a hosted service registered before it")]
    [DataRow(Recorder.HostedServiceAfter, DisplayName = "Version recorded by a hosted service registered after it")]
    public async Task A_fresh_install_opens_its_baseline_on_its_first_start_and_never_again(Recorder recorder)
    {
        var app = new StubOptions<AppOptions>(new AppOptions());
        var ui = new StubOptions<UIOptions>(new UIOptions());
        Assert.IsTrue(app.Value.IsNotInitialized(), "An install that never ran starts from the initial version.");

        await StartAndStop(app, ui, recorder);

        Assert.AreEqual(RunningVersion, app.Value.Version, "The host did not record the running version.");
        Assert.IsTrue(ui.Value.Notices.BaselinePending, "A fresh install's first start did not open its baseline.");

        // Its UI records the baseline, which closes it for good.
        await ui.SaveAsync(options => options.Notices.CaptureBaseline(["thin-client-discontinued"]));
        Assert.IsFalse(ui.Value.Notices.BaselinePending);

        await StartAndStop(app, ui, recorder);

        Assert.IsFalse(ui.Value.Notices.BaselinePending, "A later start opened the baseline again.");
        CollectionAssert.AreEqual(new[] {"thin-client-discontinued"}, ui.Value.Notices.ReadIds);
    }

    [TestMethod]
    [DataRow(Recorder.ApplicationStarted)]
    [DataRow(Recorder.HostedServiceBefore)]
    [DataRow(Recorder.HostedServiceAfter)]
    public async Task An_upgraded_install_never_opens_one(Recorder recorder)
    {
        var app = new StubOptions<AppOptions>(new AppOptions {Version = "2.3.0"});
        var ui = new StubOptions<UIOptions>(new UIOptions());

        await StartAndStop(app, ui, recorder);

        Assert.AreEqual(RunningVersion, app.Value.Version);
        Assert.IsFalse(ui.Value.Notices.BaselinePending);
        Assert.AreEqual(0, ui.SaveCount);
    }

    private static async Task StartAndStop(StubOptions<AppOptions> app, StubOptions<UIOptions> ui, Recorder recorder)
    {
        var recorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task RecordRunningVersion()
        {
            await app.SaveAsync(options => options.Version = RunningVersion);
            recorded.TrySetResult();
        }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings {DisableDefaults = true});
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IBOptions<AppOptions>>(app);
        builder.Services.AddSingleton<IBOptionsManager<AppOptions>>(app);
        builder.Services.AddSingleton<IBOptionsManager<UIOptions>>(ui);
        if (recorder == Recorder.HostedServiceBefore)
        {
            builder.Services.AddHostedService(_ => new OnStart(RecordRunningVersion));
        }

        // As BakabaseStartup registers it.
        builder.Services.AddHostedService<NoticeBaselineInitializer>();
        if (recorder == Recorder.HostedServiceAfter)
        {
            builder.Services.AddHostedService(_ => new OnStart(RecordRunningVersion));
        }

        using var host = builder.Build();
        if (recorder == Recorder.ApplicationStarted)
        {
            // AppHost: backups and migrations run on a task the callback starts, then the version.
            host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted
                .Register(() => _ = Task.Run(RecordRunningVersion));
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await host.StartAsync(timeout.Token);
        await recorded.Task.WaitAsync(timeout.Token);
        await host.StopAsync(timeout.Token);
    }

    private sealed class OnStart(Func<Task> action) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => action();
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
