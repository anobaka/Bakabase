using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionOutputDirectoryTests
{
    private IServiceProvider _services = null!;
    private AcquisitionPlanService Plans => _services.GetRequiredService<AcquisitionPlanService>();
    private DownloaderGlobalOptions Defaults => _services.GetRequiredService<IBOptions<DownloaderGlobalOptions>>().Value;

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.AddScoped<IAcquisitionPlanBuilder, DirectoryBuilder>();
            services.AddScoped(_ => new Bakabase.Service.Components.DownloadIntents.DownloadMethodRegistry([], [], null!, null!));
        });
        Defaults.DefaultDownloadPath = Path.GetTempPath();
    }

    [TestCleanup] public void Cleanup() => (_services as IDisposable)?.Dispose();

    [TestMethod]
    public async Task NewDraftsInheritOnceAndExplicitEmptyOrOverrideIsPreserved()
    {
        var inherited = await Plans.CreateDraftAsync(new("Inherit"), default);
        var empty = await Plans.CreateDraftAsync(new("Unconfigured", OutputDirectory: ""), default);
        var chosenPath = Path.Combine(Path.GetTempPath(), "chosen");
        var chosen = await Plans.CreateDraftAsync(new("Override", OutputDirectory: chosenPath), default);
        Assert.AreEqual(Path.GetTempPath(), inherited.Task.TargetDirectory);
        Assert.AreEqual("", empty.Task.TargetDirectory);
        Assert.AreEqual(chosenPath, chosen.Task.TargetDirectory);
        Defaults.DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "changed-default");
        Assert.AreEqual(Path.GetTempPath(), (await Plans.GetAsync(inherited.Id, default))!.Task.TargetDirectory);
        Assert.AreEqual("", (await Plans.GetAsync(empty.Id, default))!.Task.TargetDirectory);
        var cleared = await Plans.UpdateDraftAsync(inherited.Id, new(inherited.Task.PlanRevision, OutputDirectory: ""), default);
        Assert.AreEqual("", cleared.Task.TargetDirectory, "An edit never silently restores the global default.");
    }

    [TestMethod]
    public async Task NameSearchAndSelectionUseTheSameCapturedDirectory()
    {
        var created = await Plans.CreateAsync(new("Name", "test.directory"), default);
        Assert.AreEqual(Path.GetTempPath(), created.Task.TargetDirectory);
        Defaults.DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "later-default");
        var selected = await Plans.SelectAsync(created.Id, new("result", created.Task.PlanRevision), default);
        Assert.AreEqual(Path.GetTempPath(), selected.Plan!.OutputDirectory);
        var empty = await Plans.CreateAsync(new("No default", "test.directory", ""), default);
        Assert.AreEqual("", empty.Task.TargetDirectory);
        Assert.IsTrue(empty.Issues.Any(i => i.Code == "OUTPUT_DIRECTORY_REQUIRED"));
    }

    [TestMethod]
    public async Task PreparedPlansTreatAnExplicitEmptyPathAsAPlanDecision()
    {
        var plan = new DirectoryBuilder().BuildPlan(new("result", "One", "https://example.test/file.zip"), "One", "");
        var empty = await Plans.CreatePreparedPlanAsync(plan, []);
        var inherited = await Plans.CreatePreparedPlanAsync(plan with {OutputDirectory = null!}, []);
        Assert.AreEqual("", empty.Plan!.OutputDirectory);
        Assert.AreEqual(Path.GetTempPath(), inherited.Plan!.OutputDirectory);
    }

    private sealed class DirectoryBuilder : IAcquisitionPlanBuilder
    {
        public AcquisitionPlanBuilderDescriptor Descriptor => new("test.directory", "Test", "Test", ["manual"], "title");
        public Task<IReadOnlyList<AcquisitionPlanCandidate>> SearchAsync(string query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AcquisitionPlanCandidate>>([new("result", "One", "https://example.test/file.zip")]);
        public AcquisitionPlan BuildPlan(AcquisitionPlanCandidate candidate, string title, string outputDirectory) => new()
        {
            Title = title, OutputDirectory = outputDirectory,
            Parts = [new() {Id = "main", SelectedSourceId = "one", Sources = [new() {Id = "one", Url = candidate.Url}],
                Processing = new() {Requirement = "notRequired"}}]
        };
    }
}
