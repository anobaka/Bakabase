using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Platform;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Acquisition.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components;
using Bakabase.Service.Components.Acquisition;
using Bakabase.Service.Components.Acquisition.Connectors;
using Bakabase.Service.Components.Acquisition.Steps;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionPlatformReadinessTests
{
    private IServiceProvider _sp = null!;
    private string _root = null!;
    private readonly Launcher _launcher = new();
    private ISteamAppService Apps => _sp.GetRequiredService<ISteamAppService>();
    private AcquisitionOptions Options => _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
    private ExHentaiOptions ExHentai => _sp.GetRequiredService<IBOptions<ExHentaiOptions>>().Value;
    private IPlatformConnector Steam => _sp.GetRequiredService<IPlatformConnectorRegistry>().Get(ResourceSource.Steam)!;

    private sealed class Launcher : IPlatformClientLauncher
    {
        public bool IsAvailable { get; set; }
        public int Opens { get; private set; }
        public void Open(string url)
        {
            Opens++;
            throw new AssertFailedException("Readiness checks and server-side association must not launch Steam.");
        }
    }

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "platform-readiness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
            services.AddSingleton<IPlatformClientLauncher>(_launcher));
        Options.LibraryRootDirectory = Path.Combine(_root, "library");
        ExHentai.Accounts = null;
        await _sp.GetRequiredService<AcquisitionRecipeSeeder<BakabaseDbContext>>().SeedAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public void StandaloneServiceHostCannotLaunchDesktopClients()
    {
        var launcher = new PlatformClientLauncher(new NullGuiAdapter());
        Assert.IsFalse(launcher.IsAvailable);
        Assert.ThrowsExactly<InvalidOperationException>(() => launcher.Open("steam://install/123"));
    }

    [TestMethod]
    public async Task ExHentaiRequiresTheEffectiveAccountCookieWithoutProbingTheSite()
    {
        var step = new FetchExHentaiStep();
        var context = new AcquisitionValidationContext(_sp, null, true,
            AcquisitionLeadKind.PlatformHolding, "ExHentai:123/abcdef");
        var missing = await step.ValidateConfigurationAsync(context, default);
        Assert.IsTrue(missing.Any(i => i.Code == "exHentaiAccountMissing"));
        ExHentai.Accounts = [new() {Cookie = " "}, new() {Cookie = "secondary-account"}];
        Assert.IsTrue((await step.ValidateConfigurationAsync(context, default))
            .Any(i => i.Code == "exHentaiAccountMissing"));
        ExHentai.Cookie = "locally-configured-cookie";
        Assert.AreEqual(0, (await step.ValidateConfigurationAsync(context, default)).Count);
    }

    [DataTestMethod]
    [DataRow("Steam:123")]
    [DataRow("DLsite:RJ123456")]
    public async Task ExHentaiRejectsOtherPlatformIdentitiesEvenWithAnAccount(string value)
    {
        ExHentai.Cookie = "locally-configured-cookie";
        var issues = await new FetchExHentaiStep().ValidateConfigurationAsync(
            new AcquisitionValidationContext(_sp, null, true, AcquisitionLeadKind.PlatformHolding, value), default);
        Assert.IsTrue(issues.Any(i => i.Code == "exHentaiSourceInvalid"));
    }

    [TestMethod]
    public async Task PlatformNodeReportsMissingSteamHoldingAndHeadlessInstallCapabilityBeforeExecution()
    {
        var step = new FetchFromPlatformStep();
        var context = new AcquisitionValidationContext(_sp, null, true,
            AcquisitionLeadKind.PlatformHolding, "Steam:123");
        Assert.IsTrue((await step.ValidateConfigurationAsync(context, default))
            .Any(i => i.Code == "acquisition.steam.notInLibrary"));
        await Apps.AddOrUpdate(new SteamAppDbModel {AppId = 123, Name = "Test game"});
        Assert.IsTrue((await step.ValidateConfigurationAsync(context, default))
            .Any(i => i.Code == "acquisition.steam.clientUnavailable"));
        Assert.IsInstanceOfType<PlatformFetchOutcome.Refused>(
            await Steam.FetchAsync("123", _root, null, default));
        Assert.AreEqual(0, _launcher.Opens);
        _launcher.IsAvailable = true;
        Assert.AreEqual(0, (await step.ValidateConfigurationAsync(context, default)).Count);
        Assert.AreEqual(0, _launcher.Opens, "Validation only checks host capability; it does not open the install dialog.");
    }

    [TestMethod]
    public async Task ServerCanAssociateAnAccessibleSteamDirectoryWithoutRescanningOrLaunchingSteam()
    {
        var installed = Path.Combine(_root, "mapped-library", "game");
        Directory.CreateDirectory(installed);
        await Apps.AddOrUpdate(new SteamAppDbModel {AppId = 123, InstallPath = installed, IsInstalled = false});
        Assert.AreEqual(0, (await Steam.ValidateFetchAsync("123", default)).Count);
        Assert.AreEqual(installed, await Steam.DetectLocalPathAsync("123", default));
        var result = await Steam.FetchAsync("123", _root, null, default);
        Assert.AreEqual(installed, ((PlatformFetchOutcome.Done) result).Directory);
        var saved = (await Apps.GetByAppId(123))!;
        Assert.AreEqual(installed, saved.InstallPath, "A server scan must not erase a mapped desktop installation.");
        Assert.IsFalse(saved.IsInstalled, "Readiness and association do not rewrite installation status.");
        Assert.AreEqual(0, _launcher.Opens);
    }

    [TestMethod]
    public async Task MissingSteamDirectoryIsNotTreatedAsAlreadyDownloaded()
    {
        await Apps.AddOrUpdate(new SteamAppDbModel
        {
            AppId = 123, InstallPath = Path.Combine(_root, "not-present"), IsInstalled = true
        });
        Assert.IsTrue((await Steam.ValidateFetchAsync("123", default))
            .Any(i => i.Code == "acquisition.steam.clientUnavailable"));
        Assert.IsNull(await Steam.DetectLocalPathAsync("123", default));
    }

    [TestMethod]
    public async Task EveryVisibleLeadReceivesItsActualPlatformAndConfigurationValidation()
    {
        var resource = await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle("Multiple routes");
        await Apps.AddOrUpdate(new SteamAppDbModel {AppId = 123});
        await _sp.GetRequiredService<IResourceSourceLinkService>().EnsureLinks(resource.ResourceId,
        [
            new ResourceSourceLink {Source = ResourceSource.Steam, SourceKey = "123"},
            new ResourceSourceLink {Source = ResourceSource.ExHentai, SourceKey = "456/abcdef"}
        ]);
        await _sp.GetRequiredService<IAcquisitionLeadService>().Add(resource.ResourceId,
            new AcquisitionLeadAddInputModel {Kind = AcquisitionLeadKind.DirectUrl, Value = "https://example.invalid/file.zip"});
        var page = await _sp.GetRequiredService<AcquisitionCandidateService>().GetAsync(resource.ResourceId);
        var routes = page.Items.Single().Leads;
        Assert.IsTrue(routes.All(r => r.RecipeValidations.Count == r.ApplicableRecipeDefinitionIds.Count));
        var exh = page.Recipes.Single(r => r.Name == BuiltinAcquisitionRecipes.ExHentaiDownload).DefinitionId;
        var platform = page.Recipes.Single(r => r.Name == BuiltinAcquisitionRecipes.PlatformFetch).DefinitionId;
        var steam = routes.Single(r => r.Value == "Steam:123");
        var gallery = routes.Single(r => r.Value == "ExHentai:456/abcdef");
        Assert.AreEqual(0, steam.Id);
        Assert.AreEqual(0, gallery.Id);
        Assert.IsTrue(steam.RecipeValidations[exh].Diagnostics.Any(d => d.Code == "exHentaiSourceInvalid"));
        Assert.IsTrue(steam.RecipeValidations[platform].Diagnostics.Any(d => d.Code == "acquisition.steam.clientUnavailable"));
        Assert.IsTrue(gallery.RecipeValidations[exh].Diagnostics.Any(d => d.Code == "exHentaiAccountMissing"));
        Assert.IsFalse(gallery.RecipeValidations[exh].Diagnostics.Any(d => d.Code == "exHentaiSourceInvalid"));
        Assert.IsTrue(routes.Single(r => r.Kind == AcquisitionLeadKind.DirectUrl).RecipeValidations.Count > 0);
        Assert.AreEqual(0, _launcher.Opens);
    }

    [TestMethod]
    public async Task LocalDirectoryWorkflowReportsMissingLibraryConfigurationForActualInput()
    {
        Options.LibraryRootDirectory = null;
        var resource = await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle("Local files");
        var recipe = (await _sp.GetRequiredService<IWorkflowDefinitionService>().SearchAsync(new()
            {TriggerKind = AcquisitionWorkflowKinds.TriggerRequested}))
            .Single(r => r.Name == BuiltinAcquisitionRecipes.LocalDirectory);
        var readiness = await _sp.GetRequiredService<IWorkflowValidationService>().ValidateAsync(recipe, true,
            new AcquisitionRequestedPayload
            {
                ResourceId = resource.ResourceId, LeadKind = AcquisitionLeadKind.Manual, LeadValue = _root
            });
        Assert.IsTrue(readiness.Diagnostics.Any(d => d.Code == "acquisition.library.missing"));
        Assert.IsFalse(readiness.IsValid);
    }
}
