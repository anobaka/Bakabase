using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg.Models;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Tests;

/// <summary>
/// ffbinaries publishes no build for some runtimes (osx-arm64, windows-arm64, linux-arm). Checking an
/// installed ffmpeg for updates there must not report an error on every visit to the settings page.
/// </summary>
[TestClass]
public sealed class FfMpegLatestVersionTests
{
    private const string NoBuildForThisRuntime =
        """{"version":"6.1","permalink":"https://ffbinaries.com/api/v1/version/6.1","bin":{"none-0":{"ffmpeg":"https://example.invalid/ffmpeg.zip","ffprobe":"https://example.invalid/ffprobe.zip"}}}""";

    [TestMethod]
    public async Task UnsupportedRuntime_IsNotAvailable_NotAnError_AndCachedForTheFullDuration()
    {
        var handler = new CountingHandler(NoBuildForThisRuntime);
        var (service, logs) = await CreateService(handler);

        var latest = await service.GetLatestVersion(true, default);
        Assert.AreEqual("N/A", latest.Version);
        Assert.IsFalse(latest.CanUpdate);
        Assert.IsTrue(((FfMpegVersion) latest).UnsupportedRuntime);
        StringAssert.Contains(latest.Description, "none-0");
        Assert.IsFalse(logs.Entries.Any(e => e.Level >= LogLevel.Warning),
            "Nothing is reported as a failure.");

        await service.GetLatestVersion(true, default);
        Assert.AreEqual(1, handler.Requests, "Cached like any successful lookup, not retried after 5 minutes.");
    }

    [TestMethod]
    public async Task UnsupportedRuntime_ExplicitInstallSaysWhy()
    {
        var (service, _) = await CreateService(new CountingHandler(NoBuildForThisRuntime));
        var latest = await service.GetLatestVersion(CancellationToken.None);
        var error = Assert.ThrowsException<NotSupportedException>(() => service.CheckInstallable(latest));
        StringAssert.Contains(error.Message, "Runtime is not supported");
    }

    private static async Task<(TestFfMpegService Service, DependentComponentLatestVersionCacheTests.CapturingLoggerFactory Logs)>
        CreateService(HttpMessageHandler handler)
    {
        var sp = await TestServiceBuilder.BuildServiceProvider();
        var logs = new DependentComponentLatestVersionCacheTests.CapturingLoggerFactory();
        var service = new TestFfMpegService(logs, sp.GetRequiredService<AppService>(),
            new Factory(new HttpClient(handler)), sp.GetRequiredService<CompressedFileService>(), sp);
        return (service, logs);
    }

    private sealed class TestFfMpegService(
        ILoggerFactory loggerFactory,
        AppService appService,
        IHttpClientFactory httpClientFactory,
        CompressedFileService compressedFileService,
        IServiceProvider globalServiceProvider)
        : FfMpegService(loggerFactory, appService, httpClientFactory, compressedFileService, globalServiceProvider)
    {
        public void CheckInstallable(DependentComponentVersion latest) => OnLatestVersionNotInstallable(latest);
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CountingHandler(string json) : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
