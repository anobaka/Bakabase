using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Dependency.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class DependentComponentLifecycleTests
{
    private ServiceProvider _services = null!;

    [TestInitialize]
    public void Initialize() => _services = new ServiceCollection()
        .AddSingleton<IDependencyLocalizer>(new DependencyLocalizer()).BuildServiceProvider();

    [TestCleanup]
    public void Cleanup() => _services.Dispose();

    [TestMethod]
    public void ConstructionAndStatus_DoNotDiscoverOrInstall()
    {
        var component = new TestComponent(_services, required: true);
        Assert.AreEqual(DependentComponentStatus.NotInstalled, component.Status);
        Assert.IsNull(component.Context.Location);
        Assert.AreEqual(0, component.DiscoverCalls);
        Assert.AreEqual(0, component.InstallCalls);
    }

    [TestMethod]
    public async Task ConcurrentFirstUse_ReusesDiscoveredSystemInstallation()
    {
        var component = new TestComponent(_services) { Exists = true };
        var started = Signal();
        var release = Signal();
        component.BeforeDiscover = async ct =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
        };

        var first = component.EnsureReadyAsync(default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var others = Enumerable.Range(0, 8).Select(_ => component.EnsureReadyAsync(default)).ToArray();
        release.SetResult();
        await Task.WhenAll(others.Append(first)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(1, component.DiscoverCalls);
        Assert.AreEqual(0, component.InstallCalls);
        Assert.AreEqual(component.DefaultLocation, component.Context.Location);
    }

    [TestMethod]
    public async Task MissingOptional_IsRecoverableWithoutDownload_AndManualInstallStillWorks()
    {
        var component = new TestComponent(_services);
        await Assert.ThrowsExceptionAsync<DependencyNotInstalledException>(() => component.EnsureReadyAsync(default));
        Assert.AreEqual(0, component.InstallCalls);

        await component.Install(default);
        await component.EnsureReadyAsync(default);
        Assert.AreEqual(1, component.InstallCalls);
        Assert.AreEqual(DependentComponentStatus.Installed, component.Status);
        Assert.IsNull(component.Context.Error);
    }

    [TestMethod]
    public async Task MissingOptional_RetryFindsExternallyInstalledComponent()
    {
        var component = new TestComponent(_services);
        await Assert.ThrowsExceptionAsync<DependencyNotInstalledException>(() => component.EnsureReadyAsync(default));
        component.Exists = true;
        await component.EnsureReadyAsync(default);
        Assert.AreEqual(DependentComponentStatus.Installed, component.Status);
        Assert.AreEqual(0, component.InstallCalls);
    }

    [TestMethod]
    public async Task MissingOptional_IsNotProbedAgainOnEveryCallButExplicitDiscoveryIs()
    {
        var component = new TestComponent(_services) { RediscoveryInterval = TimeSpan.FromMinutes(1) };
        await Assert.ThrowsExceptionAsync<DependencyNotInstalledException>(() => component.EnsureReadyAsync(default));
        component.Exists = true;
        await Assert.ThrowsExceptionAsync<DependencyNotInstalledException>(() => component.EnsureReadyAsync(default));
        Assert.AreEqual(1, component.DiscoverCalls);
        await component.Discover(default);
        await component.EnsureReadyAsync(default);
        Assert.AreEqual(DependentComponentStatus.Installed, component.Status);
    }

    [TestMethod]
    public async Task OptionalCallerDoesNotWaitOutAnInstall()
    {
        var component = new TestComponent(_services);
        var started = Signal();
        var release = Signal();
        component.BeforeInstall = async ct =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        var install = component.Install(default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsExceptionAsync<DependencyNotInstalledException>(() =>
            component.EnsureReadyAsync(default).WaitAsync(TimeSpan.FromSeconds(5)));
        release.SetResult();
        await install.WaitAsync(TimeSpan.FromSeconds(5));
        await component.EnsureReadyAsync(default);
    }

    [TestMethod]
    public async Task RequiredFirstUse_ConcurrentCallersWaitForOneVerifiedInstallation()
    {
        var component = new TestComponent(_services, required: true);
        var started = Signal();
        var release = Signal();
        component.BeforeInstall = async ct =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        var first = component.EnsureReadyAsync(default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = component.EnsureReadyAsync(default);
        var discovery = component.Discover(default);
        Assert.IsFalse(second.IsCompleted);
        Assert.IsFalse(discovery.IsCompleted);
        Assert.AreEqual(DependentComponentStatus.Installing, component.Status);
        release.SetResult();
        await Task.WhenAll(first, second, discovery).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, component.InstallCalls);
        Assert.AreEqual(DependentComponentStatus.Installed, component.Status);
        Assert.AreEqual(100, component.Context.InstallationProgress);
    }

    [TestMethod]
    public async Task CancelledWaiter_DoesNotCancelAnotherCallersInstallation()
    {
        var component = new TestComponent(_services, required: true);
        var started = Signal();
        var release = Signal();
        component.BeforeInstall = async ct =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        var first = component.EnsureReadyAsync(default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var waiter = component.EnsureReadyAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await waiter);
        Assert.IsFalse(first.IsCompleted);
        release.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, component.InstallCalls);
        Assert.AreEqual(DependentComponentStatus.Installed, component.Status);
    }

    [TestMethod]
    public async Task CancelledInstallation_ReleasesWaitersAndNextCallerCanRetry()
    {
        var component = new TestComponent(_services, required: true);
        var started = Signal();
        component.BeforeInstall = async ct =>
        {
            if (component.InstallCalls == 1)
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
        };
        using var cancellation = new CancellationTokenSource();
        var first = component.EnsureReadyAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var retry = component.EnsureReadyAsync(default);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await first);
        await retry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, component.InstallCalls);
        Assert.AreEqual(DependentComponentStatus.Installed, component.Status);
        Assert.IsNull(component.Context.Error);
    }

    [TestMethod]
    public async Task FailedInstall_PreservesOriginalFailureAndRemainsRetryable()
    {
        var component = new TestComponent(_services, required: true);
        var expected = new IOException("download interrupted");
        component.BeforeInstall = _ => throw expected;
        var error = await Assert.ThrowsExceptionAsync<IOException>(() => component.EnsureReadyAsync(default));
        Assert.AreSame(expected, error);
        Assert.AreEqual(2, component.DiscoverCalls, "The failed install rediscovers what is on disk.");
        Assert.AreEqual(DependentComponentStatus.NotInstalled, component.Status);
        Assert.AreEqual(expected.Message, component.Context.Error);
        component.BeforeInstall = null;
        await component.EnsureReadyAsync(default);
        Assert.AreEqual(DependentComponentStatus.Installed, component.Status);
        Assert.IsNull(component.Context.Error);
    }

    [TestMethod]
    public async Task FailedUpdate_KeepsTheWorkingInstallationAndShowsTheError()
    {
        var component = new TestComponent(_services) { Exists = true };
        await component.Discover(default);
        var expected = new IOException("download interrupted");
        component.BeforeInstall = _ => throw expected;
        Assert.AreSame(expected, await Assert.ThrowsExceptionAsync<IOException>(() => component.Install(default)));
        Assert.AreEqual(DependentComponentStatus.Installed, component.Status);
        Assert.AreEqual(expected.Message, component.Context.Error);

        var cancelled = new CancellationTokenSource();
        component.BeforeInstall = async ct =>
        {
            await cancelled.CancelAsync();
            ct.ThrowIfCancellationRequested();
        };
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => component.Install(cancelled.Token));
        Assert.AreEqual(DependentComponentStatus.Installed, component.Status);
    }

    [TestMethod]
    public async Task FailedRediscovery_DoesNotMaskTheInstallFailure()
    {
        var component = new TestComponent(_services, required: true);
        var expected = new IOException("download interrupted");
        component.BeforeInstall = _ =>
        {
            component.BeforeDiscover = _ => throw new InvalidOperationException("probe failed");
            throw expected;
        };
        Assert.AreSame(expected,
            await Assert.ThrowsExceptionAsync<IOException>(() => component.EnsureReadyAsync(default)));
        Assert.AreEqual(DependentComponentStatus.NotInstalled, component.Status);
        Assert.AreEqual(expected.Message, component.Context.Error);
    }

    [TestMethod]
    public async Task ManualInstall_CoalescesConcurrentRequests_ButLaterRequestCanUpdate()
    {
        var component = new TestComponent(_services);
        var started = Signal();
        var release = Signal();
        component.BeforeInstall = async ct =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        var first = component.Install(default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = component.Install(default);
        Assert.IsFalse(second.IsCompleted);
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, component.InstallCalls);
        await component.Install(default);
        Assert.AreEqual(2, component.InstallCalls);
    }

    [TestMethod]
    public async Task ManualDiscovery_ReportsRemovedInstallationAndClearsStaleLocation()
    {
        var component = new TestComponent(_services) { Exists = true };
        await component.EnsureReadyAsync(default);
        component.Exists = false;
        await component.Discover(default);
        Assert.AreEqual(DependentComponentStatus.NotInstalled, component.Status);
        Assert.IsNull(component.Context.Location);
        Assert.IsNull(component.Context.Version);
    }

    [TestMethod]
    public async Task FailedDiscovery_DoesNotPoisonLaterUseOrTriggerDownload()
    {
        var component = new TestComponent(_services, required: true) { Exists = true };
        component.BeforeDiscover = _ => throw new IOException("temporary discovery error");
        await Assert.ThrowsExceptionAsync<IOException>(() => component.EnsureReadyAsync(default));
        Assert.AreEqual(0, component.InstallCalls);
        component.BeforeDiscover = null;
        await component.EnsureReadyAsync(default);
        Assert.AreEqual(DependentComponentStatus.Installed, component.Status);
        Assert.AreEqual(0, component.InstallCalls);
    }

    [TestMethod]
    public async Task UnsupportedPlatform_DoesNotDiscoverOrDownload()
    {
        var component = new TestComponent(_services, required: true) { Supported = false };
        await Assert.ThrowsExceptionAsync<PlatformNotSupportedException>(() => component.EnsureReadyAsync(default));
        await component.Discover(default);
        Assert.AreEqual(0, component.DiscoverCalls);
        Assert.AreEqual(0, component.InstallCalls);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class DependencyLocalizer : IDependencyLocalizer
    {
        public string? Dependency_Component_Name(string key) => key;
        public string? Dependency_Component_Description(string key) => key;
        public string Dependency_NotInstalled_Message(string name) => $"Install {name} in system settings.";
        public string Dependency_Installing_Message(string name) => $"Installing {name}.";
        public string Dependency_Required_Message(string name, string requiredBy) => $"{requiredBy} requires {name}.";
    }

    private sealed class TestComponent(IServiceProvider services, bool required = false)
        : DependentComponentService(NullLoggerFactory.Instance, Path.GetTempPath(),
            $"dependency-lifecycle-{Guid.NewGuid():N}", services), IDiscoverer
    {
        public bool Exists { get; set; }
        public bool Supported { get; set; } = true;
        public TimeSpan RediscoveryInterval { get; set; } = TimeSpan.Zero;
        protected override TimeSpan MissingRediscoveryInterval => RediscoveryInterval;
        public int DiscoverCalls { get; private set; }
        public int InstallCalls { get; private set; }
        public Func<CancellationToken, Task>? BeforeDiscover { get; set; }
        public Func<CancellationToken, Task>? BeforeInstall { get; set; }
        public override string Id => "test-component";
        protected override string KeyInLocalizer => "test-component";
        public override bool IsRequired => required;
        public override bool IsAvailableOnCurrentPlatform => Supported;
        protected override IDiscoverer Discoverer => this;
        public override Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct) =>
            Task.FromResult(DependentComponentVersion.Unknown);

        protected override async Task InstallCore(CancellationToken ct)
        {
            InstallCalls++;
            if (BeforeInstall != null) await BeforeInstall(ct);
            Exists = true;
        }

        async Task<(string Location, string? Version)?> IDiscoverer.Discover(string directory, CancellationToken ct)
        {
            DiscoverCalls++;
            if (BeforeDiscover != null) await BeforeDiscover(ct);
            return Exists ? (directory, "1.0.0") : null;
        }
    }
}
