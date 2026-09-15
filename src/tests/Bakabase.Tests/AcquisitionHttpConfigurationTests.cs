using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Service.Components.Acquisition.Steps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionHttpConfigurationTests
{
    private static AcquisitionWorkItem Item => new()
    {
        ResourceId = 1,
        LeadKind = AcquisitionLeadKind.DirectUrl,
        LeadValue = "https://example.invalid/file",
        Links = [new("https://example.invalid/file", DriveKind: AcquisitionDriveKind.DirectUrl)]
    };

    [TestMethod]
    public async Task LegacyTimeoutOnlyConfigurationReceivesTheNewDefaults()
    {
        var downloader = new Downloader();
        using var services = new ServiceCollection().AddSingleton<IHttpDownloader>(downloader).BuildServiceProvider();
        var context = new AcquisitionStepContext(services, NullLogger.Instance, (_, _) => Task.CompletedTask,
            "unused", """{"timeoutMinutes":60}""");

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(await new FetchHttpStep().ExecuteAsync(context, Item, CancellationToken.None));
        Assert.AreEqual(TimeSpan.FromHours(1), downloader.Request!.Timeout);
        Assert.AreEqual(4, downloader.Request.ParallelConnections);
        Assert.AreEqual(3, downloader.Request.MaxRetries);
        Assert.AreEqual(0L, downloader.Request.MaximumBytesPerSecond);
    }

    [DataTestMethod]
    [DataRow("{\"parallelConnections\":0}", "httpConnectionsInvalid")]
    [DataRow("{\"parallelConnections\":17}", "httpConnectionsInvalid")]
    [DataRow("{\"maxRetries\":-1}", "httpRetriesInvalid")]
    [DataRow("{\"maxRetries\":11}", "httpRetriesInvalid")]
    [DataRow("{\"speedLimitKiB\":-1}", "httpSpeedLimitInvalid")]
    [DataRow("{\"speedLimitKiB\":1048577}", "httpSpeedLimitInvalid")]
    [DataRow("{\"timeoutMinutes\":0}", "timeoutInvalid")]
    [DataRow("{\"timeoutMinutes\":43201}", "timeoutInvalid")]
    public async Task InvalidSettingsProduceDiagnosticsAndCannotStartTheDownloader(string json, string code)
    {
        var downloader = new Downloader();
        using var services = new ServiceCollection().AddSingleton<IHttpDownloader>(downloader).BuildServiceProvider();
        var step = new FetchHttpStep();
        var issues = await step.ValidateConfigurationAsync(new AcquisitionValidationContext(services, json), CancellationToken.None);

        Assert.AreEqual(1, issues.Count);
        Assert.AreEqual(code, issues[0].Code);
        Assert.AreEqual("workflow.validation.acquisition." + code, issues[0].MessageKey);
        var context = new AcquisitionStepContext(services, NullLogger.Instance, (_, _) => Task.CompletedTask, "unused", json);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(await step.ExecuteAsync(context, Item, CancellationToken.None));
        Assert.IsNull(downloader.Request);
    }

    [TestMethod]
    public async Task OneConnectionNoRetriesAndUnlimitedSpeedRemainExplicitChoices()
    {
        var downloader = new Downloader();
        using var services = new ServiceCollection().AddSingleton<IHttpDownloader>(downloader).BuildServiceProvider();
        var context = new AcquisitionStepContext(services, NullLogger.Instance, (_, _) => Task.CompletedTask,
            "unused", """{"parallelConnections":1,"maxRetries":0,"speedLimitKiB":0}""");

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(await new FetchHttpStep().ExecuteAsync(context, Item, CancellationToken.None));
        Assert.AreEqual(1, downloader.Request!.ParallelConnections);
        Assert.AreEqual(0, downloader.Request.MaxRetries);
        Assert.AreEqual(0L, downloader.Request.MaximumBytesPerSecond);
    }

    private sealed class Downloader : IHttpDownloader
    {
        public HttpDownloadRequest? Request;
        public Task<string> DownloadAsync(HttpDownloadRequest request, Func<int, string?, Task>? progress, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult("downloaded.bin");
        }
    }
}
