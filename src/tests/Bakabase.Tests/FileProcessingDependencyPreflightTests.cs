using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Business.Components.Dependency.Models;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class FileProcessingDependencyPreflightTests
{
    private static FileProcessingPlan Plan(bool explicitEncoding = true) => new()
    {
        Requirement = "required",
        Steps = [new() {Id = "unpack", Op = "extractArchive", Password = "not-for-errors",
            ZipPasswordCodePage = explicitEncoding ? (ZipPasswordCodePage)936 : null}]
    };

    [TestMethod]
    public async Task OrdinaryInstructionsDoNotInspectTheOptionalTool()
    {
        static IEnumerable<IDependentComponentService> DoNotEnumerate()
        {throw new AssertFailedException("Ordinary ZIP processing must not depend on Python."); yield break;}
        Assert.AreEqual(0, (await FileProcessingDependencyPreflight.InspectAsync([Plan(false)], DoNotEnumerate(), default)).Count);
    }

    [TestMethod]
    public async Task MissingOrInstallingToolBlocksOnlyTheExplicitCompatibilityPlan()
    {
        var missing = await FileProcessingDependencyPreflight.InspectAsync([Plan()], [], default);
        Assert.AreEqual(PyzipperService.ComponentId, missing.Single().ComponentId);
        Assert.AreEqual("DEPENDENCY_REQUIRED", missing.Single().Code);
        var installing = new Tool {Status = DependentComponentStatus.Installing};
        Assert.AreEqual(1, (await FileProcessingDependencyPreflight.InspectAsync([Plan()], [installing], default)).Count);
        Assert.AreEqual(0, installing.DiscoverCalls);
    }

    [TestMethod]
    public async Task ReadonlyDiscoveryCanFindTheConfiguredToolButNeverInstalls()
    {
        var tool = new Tool {Found = true};
        Assert.AreEqual(0, (await FileProcessingDependencyPreflight.InspectAsync([Plan(),Plan()], [tool], default)).Count);
        Assert.AreEqual(1, tool.DiscoverCalls);
        Assert.AreEqual(0, (await FileProcessingDependencyPreflight.InspectAsync([Plan()], [tool], default)).Count);
        Assert.AreEqual(1, tool.DiscoverCalls);
    }

    [TestMethod]
    public async Task DiscoveryErrorsAreRedactedAndCancellationPropagates()
    {
        var tool = new Tool {Failure = new IOException("credentials and a private path")};
        var issue = (await FileProcessingDependencyPreflight.InspectAsync([Plan()], [tool], default)).Single();
        Assert.IsFalse(issue.Message.Contains("credentials"));
        tool.Failure = new OperationCanceledException();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => FileProcessingDependencyPreflight.InspectAsync([Plan()], [tool], default));
    }

    [TestMethod]
    public async Task SelectedSourceFallbackMatchesTheCompiledPlanAndIgnoresUnselectedMirrors()
    {
        var part = new AcquisitionPlanPart { Id = "part", SelectedSourceId = "plain", Sources = [
            new() { Id = "plain", Processing = Plan(false) },
            new() { Id = "encoded", Processing = Plan() }
        ] };
        Assert.AreEqual(0, (await FileProcessingDependencyPreflight.InspectAsync(
            [AcquisitionPlanCompiler.Processing(part)], [], default)).Count);
        Assert.AreEqual("DEPENDENCY_REQUIRED", (await FileProcessingDependencyPreflight.InspectAsync(
            [AcquisitionPlanCompiler.Processing(part with { SelectedSourceId = "encoded" })], [], default)).Single().Code);
        Assert.AreEqual(0, (await FileProcessingDependencyPreflight.InspectAsync(
            [AcquisitionPlanCompiler.Processing(part with { SelectedSourceId = "encoded", Processing = Plan(false) })], [], default)).Count);
    }

    [TestMethod]
    public async Task FrozenProcessingNodeReportsTheDependencyBeforeExecuting()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var activity = new AcquisitionPlanProcessingActivity();
        var issues = await activity.ValidateConfigAsync(new WorkflowValidationContext
        {
            Services = services, TriggerKind = "manual", IsExecution = true,
            ConfigJson = JsonSerializer.Serialize(new AcquisitionPlanProcessingConfig("part", Plan()), WorkflowJson.Options)
        }, default);
        Assert.AreEqual("DEPENDENCY_REQUIRED", issues.Single().Code);
        Assert.IsFalse(issues.Single().Message.Contains("not-for-errors"));
    }

    private sealed class Tool : IDependentComponentService
    {
        public string Id => PyzipperService.ComponentId;
        public string DisplayName => "ZIP compatibility";
        public string? Description => null;
        public string DefaultLocation => "";
        public bool IsRequired => false;
        public bool IsAvailableOnCurrentPlatform => true;
        public bool CanAutoInstallOnCurrentPlatform => true;
        public IReadOnlyList<DependentComponentTarget> SupportedTargets => [];
        public IReadOnlyList<string> DependencyIds => [];
        public DependentComponentStatus Status { get; set; } = DependentComponentStatus.NotInstalled;
        public DependentComponentContext Context => throw new AssertFailedException("No runtime context requested.");
        public event Func<DependentComponentContext,Task>? OnStateChange {add {} remove {}}
        public bool Found {get;init;}
        public Exception? Failure {get;set;}
        public int DiscoverCalls {get;private set;}
        public Task EnsureReadyAsync(CancellationToken ct) => throw new AssertFailedException("Must not install.");
        public Task Install(CancellationToken ct) => throw new AssertFailedException("Must not install.");
        public Task<DependentComponentVersion> GetLatestVersion(bool fromCache, CancellationToken ct) => throw new AssertFailedException("Must not check updates.");
        public Task Discover(CancellationToken ct)
        {
            DiscoverCalls++;
            if (Failure != null) throw Failure;
            if (Found) Status = DependentComponentStatus.Installed;
            return Task.CompletedTask;
        }
    }
}
