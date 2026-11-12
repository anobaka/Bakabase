using Bakabase.InsideWorld.Business.Components.Dependency;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Dependency.Extensions;
using Bakabase.InsideWorld.Business.Components.Dependency.Models;
using Bakabase.TestKit.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class DependentComponentPlatformTests
{
    [DataTestMethod]
    [DataRow("windows", "x64", null)]
    [DataRow("windows", "arm64", null)]
    [DataRow("linux", "x64", "glibc")]
    [DataRow("linux", "arm64", "glibc")]
    [DataRow("macos", "x64", null)]
    [DataRow("macos", "arm64", null)]
    public void CloudPythonTargetsMatchItsManagedArchiveMatrix(string os, string architecture, string? runtime)
    {
        var target = DependentComponentPlatforms.CloudPython.Single(t =>
            DependentComponentPlatforms.Matches(t, os, architecture, runtime));
        Assert.IsTrue(target.CanAutoInstall);
    }

    [DataTestMethod]
    [DataRow("linux", "arm64", "musl")]
    [DataRow("linux", "x64", "musl")]
    [DataRow("windows", "x86", null)]
    [DataRow("freebsd", "x64", null)]
    public void CloudPythonRejectsTargetsWithoutAnInstallerAsset(string os, string architecture, string? runtime) =>
        Assert.IsFalse(DependentComponentPlatforms.CloudPython.Any(t =>
            DependentComponentPlatforms.Matches(t, os, architecture, runtime)));

    [DataTestMethod]
    [DataRow("windows", "x64", true)]
    [DataRow("windows", "arm64", false)]
    [DataRow("linux", "arm64", true)]
    [DataRow("linux", "arm", false)]
    [DataRow("macos", "x64", true)]
    [DataRow("macos", "arm64", false)]
    public void FfMpegSeparatesRunnableTargetsFromAutomaticDownloads(string os, string architecture, bool canInstall)
    {
        var target = DependentComponentPlatforms.FfMpeg.Single(t =>
            DependentComponentPlatforms.Matches(t, os, architecture, null));
        Assert.AreEqual(canInstall, target.CanAutoInstall);
    }

    [TestMethod]
    public void SevenZipAndLocaleEmulatorDeclareOnlyTheirSupportedArchitectures()
    {
        Assert.IsTrue(DependentComponentPlatforms.SevenZip.Any(t => t.Os == "windows" && t.Architecture == "arm64"));
        Assert.IsTrue(DependentComponentPlatforms.SevenZip.Any(t => t.Os == "macos" && t.Architecture == "arm64"));
        Assert.IsFalse(DependentComponentPlatforms.SevenZip.Any(t => t.Os == "linux" && t.Architecture == "x86"));
        Assert.IsTrue(DependentComponentPlatforms.LocaleEmulator.All(t => t.Os == "windows"));
        Assert.IsFalse(DependentComponentPlatforms.LocaleEmulator.Any(t => t.Architecture == "arm64"));
        Assert.AreEqual(6, DependentComponentPlatforms.BaiduPcsGo.Count);
    }

    [TestMethod]
    public async Task ManualRuntimeCanBeDiscoveredAndUsedWhileAutomaticInstallationIsUnavailable()
    {
        using var services = Services();
        var component = new Component(services) { Installed = true };
        Assert.IsTrue(component.IsAvailableOnCurrentPlatform);
        Assert.IsFalse(component.CanAutoInstallOnCurrentPlatform);
        await component.EnsureReadyAsync(default);
        Assert.AreEqual("1.0", component.Context.Version);
        Assert.AreEqual(0, component.InstallCalls);
        await Assert.ThrowsExceptionAsync<PlatformNotSupportedException>(() => component.Install(default));
        Assert.AreEqual(0, component.InstallCalls);
        Assert.IsFalse((await component.GetLatestVersion(true, default)).CanUpdate);
    }

    [TestMethod]
    public void ApiMetadataComesFromTheSameDeclarationAndDependencyGraphAsInstallation()
    {
        using var services = Services();
        var prerequisite = new Prerequisite(services);
        using var application = new ServiceCollection().AddSingleton(prerequisite)
            .AddSingleton<IDependencyLocalizer>(new TestDependencyLocalizer()).BuildServiceProvider();
        var component = new Component(application);
        var context = component.BuildContextDto();
        Assert.AreSame(component.SupportedTargets, context.SupportedTargets);
        Assert.AreEqual(component.IsAvailableOnCurrentPlatform, context.IsAvailableOnCurrentPlatform);
        Assert.AreEqual(component.CanAutoInstallOnCurrentPlatform, context.CanAutoInstallOnCurrentPlatform);
        Assert.AreEqual(DependentComponentPlatforms.CurrentOs, context.CurrentOs);
        Assert.AreEqual(DependentComponentPlatforms.CurrentArchitecture, context.CurrentArchitecture);
        Assert.AreEqual(DependentComponentPlatforms.CurrentRuntime, context.CurrentRuntime);
        CollectionAssert.AreEqual(new[] { prerequisite.Id }, context.DependencyIds.ToArray());
    }

    private static ServiceProvider Services() => new ServiceCollection()
        .AddSingleton<IDependencyLocalizer>(new TestDependencyLocalizer()).BuildServiceProvider();

    private sealed class Component(IServiceProvider services)
        : DependentComponentService(NullLoggerFactory.Instance, Path.GetTempPath(), "platform-fixture", services), IDiscoverer
    {
        public override string Id => "platform-fixture";
        protected override string KeyInLocalizer => "ffmpeg";
        public override bool IsRequired => false;
        protected override IDiscoverer Discoverer => this;
        public bool Installed { get; init; }
        public int InstallCalls { get; private set; }
        protected override IEnumerable<Type> Dependencies => services.GetService<Prerequisite>() == null ? [] : [typeof(Prerequisite)];
        public override IReadOnlyList<DependentComponentTarget> SupportedTargets { get; } =
            Array.AsReadOnly(new[] { new DependentComponentTarget
            {
                Os = DependentComponentPlatforms.CurrentOs,
                Architecture = DependentComponentPlatforms.CurrentArchitecture,
                CanAutoInstall = false
            } });
        protected override Task InstallCore(CancellationToken ct) { InstallCalls++; return Task.CompletedTask; }
        public override Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct) =>
            Task.FromResult(new DependentComponentVersion { Version = "2.0" });
        Task<(string Location, string? Version)?> IDiscoverer.Discover(string directory, CancellationToken ct) =>
            Task.FromResult<(string Location, string? Version)?>(Installed ? (directory, "1.0") : null);
    }

    private sealed class Prerequisite(IServiceProvider services)
        : DependentComponentService(NullLoggerFactory.Instance, Path.GetTempPath(), "prerequisite-fixture", services), IDiscoverer
    {
        public override string Id => "prerequisite-fixture";
        protected override string KeyInLocalizer => "7z";
        public override bool IsRequired => false;
        protected override IDiscoverer Discoverer => this;
        protected override Task InstallCore(CancellationToken ct) => Task.CompletedTask;
        public override Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct) =>
            Task.FromResult(new DependentComponentVersion { Version = "1.0" });
        Task<(string Location, string? Version)?> IDiscoverer.Discover(string directory, CancellationToken ct) =>
            Task.FromResult<(string Location, string? Version)?>(null);
    }
}
