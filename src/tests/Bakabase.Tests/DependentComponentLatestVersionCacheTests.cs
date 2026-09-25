using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Tests;

/// <summary>
/// The settings page asks for every component's latest version each time it opens; the lookup is
/// cached server-side, and whether an update is offered is decided on every read against what is
/// installed at that moment.
/// </summary>
[TestClass]
public sealed class DependentComponentLatestVersionCacheTests
{
    private ServiceProvider _services = null!;

    [TestInitialize]
    public void Initialize() => _services = new ServiceCollection()
        .AddSingleton<IDependencyLocalizer>(new DependencyLocalizer()).BuildServiceProvider();

    [TestCleanup]
    public void Cleanup() => _services.Dispose();

    [TestMethod]
    public async Task ReadsWithinTheCacheDuration_LookUpOnce()
    {
        var component = new TestComponent(_services) { Latest = "6.1" };
        await component.GetLatestVersion(true, default);
        component.TestClock.Advance(TimeSpan.FromMinutes(59));
        var second = await component.GetLatestVersion(true, default);
        Assert.AreEqual(1, component.LookupCalls);
        Assert.AreEqual("6.1", second.Version);
    }

    [TestMethod]
    public async Task ReadAfterTheCacheDuration_LooksUpAgain()
    {
        var component = new TestComponent(_services) { Latest = "6.1" };
        await component.GetLatestVersion(true, default);
        component.TestClock.Advance(TimeSpan.FromHours(1));
        component.Latest = "6.2";
        var second = await component.GetLatestVersion(true, default);
        Assert.AreEqual(2, component.LookupCalls);
        Assert.AreEqual("6.2", second.Version);
    }

    [TestMethod]
    public async Task ReadNotFromCache_AlwaysLooksUp()
    {
        var component = new TestComponent(_services) { Latest = "6.1" };
        await component.GetLatestVersion(true, default);
        await component.GetLatestVersion(false, default);
        Assert.AreEqual(2, component.LookupCalls);
        await component.GetLatestVersion(true, default);
        Assert.AreEqual(2, component.LookupCalls, "A forced lookup refreshes the cache too.");
    }

    [TestMethod]
    public async Task CanUpdate_IsDecidedAgainstTheCurrentlyInstalledVersion()
    {
        var component = new TestComponent(_services) { Latest = "6.1", Installed = "6.0" };
        await component.Discover(default);
        var before = await component.GetLatestVersion(true, default);
        Assert.IsTrue(before.CanUpdate);

        component.Installed = "6.1-static";
        await component.Discover(default);
        var after = await component.GetLatestVersion(true, default);
        Assert.IsFalse(after.CanUpdate);
        Assert.AreEqual(1, component.LookupCalls);
        Assert.IsTrue(before.CanUpdate, "A verdict already handed out is not changed afterwards.");
        Assert.IsFalse(component.LastLookedUp!.CanUpdate, "The cached lookup never carries a verdict.");

        component.Installed = null;
        await component.Discover(default);
        Assert.IsTrue((await component.GetLatestVersion(true, default)).CanUpdate,
            "Nothing installed: the install is offered.");
    }

    [TestMethod]
    public async Task InstalledVersionRecognized_TellsUpToDateFromNotCompared()
    {
        var component = new TestComponent(_services) { Latest = "6.1", Installed = "6.1" };
        await component.Discover(default);
        var upToDate = await component.GetLatestVersion(true, default);
        Assert.IsFalse(upToDate.CanUpdate);
        Assert.IsTrue(upToDate.InstalledVersionRecognized);

        component.Installed = "2021-01-31-git-6c92557756-full_build-www.gyan.dev";
        await component.Discover(default);
        var notCompared = await component.GetLatestVersion(true, default);
        Assert.IsFalse(notCompared.CanUpdate);
        Assert.IsFalse(notCompared.InstalledVersionRecognized);

        component.Installed = null;
        await component.Discover(default);
        Assert.IsTrue((await component.GetLatestVersion(true, default)).InstalledVersionRecognized,
            "Nothing installed is not an unrecognised version.");
    }

    [TestMethod]
    public async Task VerdictFromTheLookupItself_IsIgnored()
    {
        var component = new TestComponent(_services) { Latest = "6.1", Installed = "6.1", LookupSaysCanUpdate = true };
        await component.Discover(default);
        Assert.IsFalse((await component.GetLatestVersion(true, default)).CanUpdate);
    }

    [TestMethod]
    public async Task CachedResult_KeepsTheComponentsVersionType()
    {
        var component = new TestComponent(_services) { Latest = "6.1" };
        var version = await component.GetLatestVersion(true, default);
        Assert.IsInstanceOfType<TestVersion>(version);
        Assert.AreEqual("https://example.invalid/6.1.zip", ((TestVersion) version).DownloadUrl);
    }

    [TestMethod]
    public async Task FailedLookup_OffersNothing_AndIsRetriedOnlyAfterTheFailureDuration()
    {
        var component = new TestComponent(_services) { Latest = "6.1", Failure = new HttpRequestException("offline") };
        var failed = await component.GetLatestVersion(true, default);
        Assert.IsFalse(failed.CanUpdate);
        Assert.IsNull(failed.Version);

        component.Failure = null;
        component.TestClock.Advance(TimeSpan.FromMinutes(4));
        await component.GetLatestVersion(true, default);
        Assert.AreEqual(1, component.LookupCalls);

        component.TestClock.Advance(TimeSpan.FromMinutes(1));
        var recovered = await component.GetLatestVersion(true, default);
        Assert.AreEqual(2, component.LookupCalls);
        Assert.AreEqual("6.1", recovered.Version);
        Assert.IsTrue(recovered.CanUpdate);
    }

    [TestMethod]
    public async Task FailedLookup_NetworkErrorsAreWarnings_OthersAreErrors()
    {
        var component = new TestComponent(_services) { Failure = new HttpRequestException("offline") };
        await component.GetLatestVersion(true, default);
        Assert.AreEqual(LogLevel.Warning, component.Logs.Single().Level);

        component.Failure = new InvalidOperationException("unexpected shape");
        await component.GetLatestVersion(false, default);
        Assert.AreEqual(LogLevel.Error, component.Logs.Last().Level);
        Assert.AreEqual(2, component.Logs.Count);
    }

    [TestMethod]
    public async Task CancelledLookup_IsNotCachedAsAFailure()
    {
        var component = new TestComponent(_services) { Latest = "6.1" };
        using var cts = new CancellationTokenSource();
        component.BeforeLookup = async ct =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
        };
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            component.GetLatestVersion(true, cts.Token));
        Assert.AreEqual(0, component.Logs.Count);

        component.BeforeLookup = null;
        var version = await component.GetLatestVersion(true, default);
        Assert.AreEqual("6.1", version.Version);
        Assert.AreEqual(2, component.LookupCalls);
    }

    [TestMethod]
    public async Task UnavailablePlatform_NeverOffersAnUpdate()
    {
        var component = new TestComponent(_services) { Latest = "6.1", Supported = false };
        Assert.IsFalse((await component.GetLatestVersion(true, default)).CanUpdate);
    }

    [TestMethod]
    public async Task ConcurrentReads_LookUpOnce()
    {
        var component = new TestComponent(_services) { Latest = "6.1" };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        component.BeforeLookup = async ct =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
        };

        var first = component.GetLatestVersion(true, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var others = Enumerable.Range(0, 8).Select(_ => component.GetLatestVersion(true, default)).ToArray();
        release.SetResult();
        var results = await Task.WhenAll(others.Append(first)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(1, component.LookupCalls);
        Assert.IsTrue(results.All(r => r.Version == "6.1" && r.CanUpdate));
    }

    private sealed record TestVersion : DependentComponentVersion
    {
        public string DownloadUrl { get; set; } = null!;
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class DependencyLocalizer : IDependencyLocalizer
    {
        public string? Dependency_Component_Name(string key) => key;
        public string? Dependency_Component_Description(string key) => key;
        public string Dependency_NotInstalled_Message(string name) => $"Install {name} in system settings.";
        public string Dependency_Installing_Message(string name) => $"Installing {name}.";
        public string Dependency_Required_Message(string name, string requiredBy) => $"{requiredBy} requires {name}.";
    }

    internal sealed class CapturingLoggerFactory : ILoggerFactory, ILogger
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    private sealed class TestComponent : DependentComponentService, IDiscoverer
    {
        private readonly CapturingLoggerFactory _loggerFactory;

        public TestComponent(IServiceProvider services) : this(services, new CapturingLoggerFactory())
        {
        }

        private TestComponent(IServiceProvider services, CapturingLoggerFactory loggerFactory)
            : base(loggerFactory, Path.GetTempPath(), $"dependency-latest-{Guid.NewGuid():N}", services)
        {
            _loggerFactory = loggerFactory;
        }

        public ManualClock TestClock { get; } = new();
        protected override TimeProvider Clock => TestClock;

        public string? Latest { get; set; }
        public string? Installed { get; set; }
        public bool LookupSaysCanUpdate { get; set; }
        public Exception? Failure { get; set; }
        public bool Supported { get; set; } = true;
        public Func<CancellationToken, Task>? BeforeLookup { get; set; }
        public int LookupCalls { get; private set; }
        public DependentComponentVersion? LastLookedUp { get; private set; }
        public List<(LogLevel Level, string Message)> Logs => _loggerFactory.Entries.ToList();

        public override string Id => "test-component";
        protected override string KeyInLocalizer => "test-component";
        public override bool IsRequired => false;
        public override bool IsAvailableOnCurrentPlatform => Supported;
        protected override IDiscoverer Discoverer => this;

        public override async Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct)
        {
            LookupCalls++;
            if (BeforeLookup != null) await BeforeLookup(ct);
            if (Failure != null) throw Failure;
            LastLookedUp = new TestVersion
            {
                Version = Latest!,
                DownloadUrl = $"https://example.invalid/{Latest}.zip",
                CanUpdate = LookupSaysCanUpdate
            };
            return LastLookedUp;
        }

        protected override Task InstallCore(CancellationToken ct) => Task.CompletedTask;

        Task<(string Location, string? Version)?> IDiscoverer.Discover(string directory, CancellationToken ct) =>
            Task.FromResult<(string Location, string? Version)?>(Installed == null ? null : (directory, Installed));
    }
}
