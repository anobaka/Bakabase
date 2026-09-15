using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Platform;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Acquisition.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition;
using Bakabase.Service.Components.Acquisition.Downloads;
using Bakabase.Service.Components.Acquisition.Steps;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionCandidateTests
{
    private IServiceProvider _sp = null!;
    private AcquisitionCandidateService Candidates => _sp.GetRequiredService<AcquisitionCandidateService>();
    private BakabaseDbContext Db => _sp.GetRequiredService<BakabaseDbContext>();

    // Validation may inspect locally cached account/holding state, but browsing must never
    // enumerate a remote library, probe the installation or ask a platform to fetch files.
    private sealed class NoProbeRegistry : IPlatformConnectorRegistry
    {
        public IReadOnlyCollection<ResourceSource> Sources { get; } =
            [ResourceSource.DLsite, ResourceSource.Steam, ResourceSource.ExHentai];

        public IPlatformConnector? Get(ResourceSource source) => new NoProbeConnector(source);
    }

    private sealed class NoProbeConnector(ResourceSource source) : IPlatformConnector
    {
        public ResourceSource Source => source;
        public bool CanFetch => true;
        public Task<IReadOnlyList<PlatformHolding>> EnumerateHoldingsAsync(CancellationToken ct) =>
            throw new AssertFailedException("Browsing candidates must not enumerate platform libraries.");
        public Task<string?> DetectLocalPathAsync(string key, CancellationToken ct) =>
            throw new AssertFailedException("Browsing candidates must not probe installations.");
        public Task<PlatformFetchOutcome> FetchAsync(string key, string path,
            Func<int, string?, Task>? progress, CancellationToken ct) =>
            throw new AssertFailedException("Browsing candidates must not start platform downloads.");
    }

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
            services.AddSingleton<IPlatformConnectorRegistry, NoProbeRegistry>());
        await _sp.GetRequiredService<AcquisitionRecipeSeeder<BakabaseDbContext>>().SeedAsync();
    }

    private async Task<int> Missing(string name) =>
        (await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(name)).ResourceId;

    private Task Identity(int id, ResourceSource source, string key) =>
        _sp.GetRequiredService<IResourceSourceLinkService>().EnsureLinks(id,
            [new ResourceSourceLink {Source = source, SourceKey = key}]);

    private async Task AddLead(int id, AcquisitionLeadKind kind, string value) =>
        await _sp.GetRequiredService<IAcquisitionLeadService>().Add(id,
            new AcquisitionLeadAddInputModel {Kind = kind, Value = value});

    [TestMethod]
    public async Task PlatformIdentityIsAnUnknownRoute_AndUnsupportedPlatformsCannotBeStarted()
    {
        var dlsite = await Missing("An unpurchased catalog work");
        var pixiv = await Missing("A Pixiv identity");
        var catalog = await Missing("Metadata only");
        await Identity(dlsite, ResourceSource.DLsite, "RJ00000001");
        await Identity(pixiv, ResourceSource.Pixiv, "12345");
        await _sp.GetRequiredService<IResourceExternalIdentityService>().EnsureIdentities(catalog,
            [new ResourceExternalIdentity {ThirdPartyId = ThirdPartyId.Bangumi, ExternalId = "67890"}]);
        var before = await Db.Set<AcquisitionTaskDbModel>().CountAsync();

        var page = await Candidates.SearchAsync();

        var known = page.Items.Single(r => r.ResourceId == dlsite).Leads.Single();
        Assert.IsTrue(known.IsDerived);
        Assert.AreEqual(0, known.Id);
        Assert.AreEqual("DLsite:RJ00000001", known.Value);
        Assert.IsTrue(FetchFromPlatformStep.TryReadLead(known.Value, out var source, out var key));
        Assert.AreEqual(ResourceSource.DLsite, source);
        Assert.AreEqual("RJ00000001", key);
        Assert.AreEqual("unknown", known.Availability);
        Assert.AreEqual("supported", known.Capability,
            "Supported only describes the implemented route, not a purchase or successful download.");
        Assert.IsNotNull(known.DefaultRecipeDefinitionId);

        var unsupported = page.Items.Single(r => r.ResourceId == pixiv).Leads.Single();
        Assert.AreEqual("unknown", unsupported.Availability);
        Assert.AreEqual("unsupportedPlatform", unsupported.Capability);
        Assert.AreEqual(0, unsupported.ApplicableRecipeDefinitionIds.Count);
        Assert.AreEqual(0, page.Items.Single(r => r.ResourceId == catalog).Leads.Count);
        Assert.AreEqual(before, await Db.Set<AcquisitionTaskDbModel>().CountAsync(),
            "Reading routes cannot create acquisition tasks.");
    }

    [TestMethod]
    public async Task FiltersApplyBeforePagination_AndKeywordUsesThePathlessResourcesName()
    {
        var first = await Missing("Overview sample first");
        await Missing("An unrelated resource");
        var third = await Missing("Overview sample third");
        var noLead = await Missing("Overview sample without a route");
        await AddLead(first, AcquisitionLeadKind.DirectUrl, "https://example.invalid/one.zip");
        await AddLead(third, AcquisitionLeadKind.SharedPage, "https://example.invalid/thread/three");
        await _sp.GetRequiredService<IResourceService>().AddOrPutRange([
            new Resource {Path = "/not-probed/overview-materialized", Status = ResourceStatus.Active}
        ]);

        var firstPage = await Candidates.SearchAsync("Overview sample", page: 1, pageSize: 1,
            filter: "withSources");
        var secondPage = await Candidates.SearchAsync("Overview sample", page: 2, pageSize: 1,
            filter: "withSources");

        Assert.AreEqual(2, firstPage.TotalCount);
        Assert.AreEqual(third, firstPage.Items.Single().ResourceId);
        Assert.AreEqual(first, secondPage.Items.Single().ResourceId);
        Assert.AreEqual("Overview sample first", secondPage.Items.Single().ResourceName);
        var without = await Candidates.SearchAsync("Overview sample", filter: "withoutSources");
        Assert.AreEqual(noLead, without.Items.Single().ResourceId);
        var all = await Candidates.SearchAsync();
        Assert.AreEqual(4, all.TotalCount, "The local-path resource is outside this page's purpose.");
        Assert.AreEqual(0, (await Candidates.SearchAsync(page: int.MaxValue)).Items.Count);
    }

    [TestMethod]
    public async Task DefaultRecipeAndItsStepsAgreeWithTheRecipeUsedToCreateATask()
    {
        var custom = await _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(
            new WorkflowDefinitionCreationInputModel
            {
                Name = "My direct links go through the inbox",
                TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
                Activities = new[] {AcquisitionStepKinds.WaitForInbox, AcquisitionStepKinds.Place,
                        AcquisitionStepKinds.Materialize}
                    .Select(k => new WorkflowActivityInputModel {Kind = k}).ToList()
            });
        var options = _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
        options.RecipeByLeadKind[AcquisitionLeadKind.DirectUrl] = custom.Name;
        options.InboxDirectory = Path.Combine(Path.GetTempPath(), "candidate-inbox-" + Guid.NewGuid().ToString("N"));
        options.LibraryRootDirectory = Path.Combine(Path.GetTempPath(), "candidate-library-" + Guid.NewGuid().ToString("N"));
        options.Concurrency = 1;
        var busyResource = await Missing("Already running");
        var target = await Missing("A direct link to queue");
        await AddLead(target, AcquisitionLeadKind.DirectUrl, "https://example.invalid/archive.zip");

        // Occupy the sole slot so CreateAsync resolves its real default and queues without ever
        // executing a step, opening a browser or starting a download.
        Db.Set<AcquisitionTaskDbModel>().Add(new AcquisitionTaskDbModel
        {
            ResourceId = busyResource, Status = AcquisitionStatus.Running,
            RecipeDefinitionId = custom.Id, LeadKind = AcquisitionLeadKind.Manual,
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        });
        await Db.SaveChangesAsync();

        var lead = (await Candidates.SearchAsync()).Items.Single(r => r.ResourceId == target).Leads.Single();
        Assert.AreEqual(custom.Id, lead.DefaultRecipeDefinitionId);
        Assert.AreEqual(custom.Name, lead.DefaultRecipeName);
        Assert.AreEqual("workflow", lead.Method);
        CollectionAssert.Contains(lead.ApplicableRecipeDefinitionIds, custom.Id);
        CollectionAssert.Contains((await _sp.GetRequiredService<IAcquisitionService>().GetRecipesAsync())
            .Single(r => r.DefinitionId == custom.Id).ApplicableLeadKinds, AcquisitionLeadKind.DirectUrl);

        var task = await _sp.GetRequiredService<IAcquisitionService>().CreateAsync(target,
            lead.Kind, lead.Value, lead.Id);
        Assert.AreEqual(lead.DefaultRecipeDefinitionId, task.RecipeDefinitionId);
        Assert.IsNull(task.WorkflowRunId, "This regression must not run any acquisition steps.");
        var reread = (await Candidates.SearchAsync()).Items.Single(r => r.ResourceId == target);
        Assert.AreEqual(task.Id, reread.ActiveTaskId);
        Assert.AreEqual(AcquisitionStatus.Pending, reread.ActiveTaskStatus);
    }

    [TestMethod]
    public async Task MissingDefaultsAndIncompatibleRecipesAreNotPresentedAsAutomaticFallbacks()
    {
        var target = await Missing("A manual direct link");
        await AddLead(target, AcquisitionLeadKind.DirectUrl, "https://example.invalid/file.zip");
        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value
            .RecipeByLeadKind[AcquisitionLeadKind.DirectUrl] = "A deleted recipe";

        var page = await Candidates.SearchAsync();
        var lead = page.Items.Single().Leads.Single();
        Assert.AreEqual("A deleted recipe", lead.DefaultRecipeName);
        Assert.IsNull(lead.DefaultRecipeDefinitionId);
        Assert.IsTrue(lead.ApplicableRecipeDefinitionIds.Count > 0,
            "Other routes can still be selected explicitly; they are not the effective default.");
        var platformRecipe = page.Recipes.Single(r => r.Name == BuiltinAcquisitionRecipes.PlatformFetch);
        CollectionAssert.DoesNotContain(lead.ApplicableRecipeDefinitionIds, platformRecipe.DefinitionId);

        var unsupported = await Missing("Only an unsupported platform");
        await Identity(unsupported, ResourceSource.Pixiv, "99999");
        Assert.AreEqual(unsupported, (await Candidates.SearchAsync(filter: "unsupported"))
            .Items.Single().ResourceId);
    }
    [DataTestMethod]
    [DataRow(AcquisitionStatus.Pending)]
    [DataRow(AcquisitionStatus.Running)]
    [DataRow(AcquisitionStatus.Waiting)]
    public async Task SingleResourceUsesTheOverviewsRoutesRecipesAndActiveTask(AcquisitionStatus status)
    {
        var target = await Missing("The same resource name");
        await Missing("The same resource name");
        await Identity(target, ResourceSource.Steam, "123");
        await Identity(target, ResourceSource.Pixiv, "456");
        await AddLead(target, AcquisitionLeadKind.DirectUrl, "https://example.invalid/single.zip");
        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value
            .RecipeByLeadKind[AcquisitionLeadKind.DirectUrl] = "A deleted default";
        var recipe = (await _sp.GetRequiredService<IAcquisitionService>().GetRecipesAsync()).First();
        var active = new AcquisitionTaskDbModel
        {
            ResourceId = target, Status = status, RecipeDefinitionId = recipe.DefinitionId,
            LeadKind = AcquisitionLeadKind.PlatformHolding, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        };
        Db.Set<AcquisitionTaskDbModel>().Add(active);
        await Db.SaveChangesAsync();
        Db.Set<AcquisitionTaskDbModel>().Add(new AcquisitionTaskDbModel
        {
            ResourceId = target, Status = AcquisitionStatus.Completed, RecipeDefinitionId = recipe.DefinitionId,
            LeadKind = AcquisitionLeadKind.PlatformHolding, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        });
        await Db.SaveChangesAsync();

        var overview = await Candidates.SearchAsync();
        var detail = await Candidates.GetAsync(target);

        Assert.AreEqual(1, detail.TotalCount);
        Assert.AreEqual(1, detail.Page);
        Assert.AreEqual(1, detail.PageSize);
        var item = detail.Items.Single();
        Assert.AreEqual(JsonSerializer.Serialize(overview.Items.Single(r => r.ResourceId == target)),
            JsonSerializer.Serialize(item), "Both entry points must describe the same routes and safeguards.");
        Assert.AreEqual(JsonSerializer.Serialize(overview.Recipes), JsonSerializer.Serialize(detail.Recipes));
        Assert.AreEqual(active.Id, item.ActiveTaskId);
        Assert.AreEqual(status, item.ActiveTaskStatus);
        Assert.AreEqual("Steam:123", item.Leads.Single(l => l.SourceName == "Steam").Value);
        Assert.AreEqual("unsupportedPlatform", item.Leads.Single(l => l.SourceName == "Pixiv").Capability);
        Assert.IsNull(item.Leads.Single(l => l.Kind == AcquisitionLeadKind.DirectUrl).DefaultRecipeDefinitionId);
        Assert.AreEqual(2, await Db.Set<AcquisitionTaskDbModel>().CountAsync(),
            "Reading either entry point must not start another task.");
    }

    [TestMethod]
    public async Task SingleResourceReturnsEmptyForLocalAndMissingResources_ButKeepsRecipes()
    {
        var resourceService = _sp.GetRequiredService<IResourceService>();
        const string path = "/not-probed/single-candidate-local";
        await resourceService.AddOrPutRange([new Resource {Path = path, Status = ResourceStatus.Active}]);
        var local = (await resourceService.GetAll(r => r.Path == path)).Single();
        await Identity(local.Id, ResourceSource.Steam, "999");
        var recipes = await _sp.GetRequiredService<IAcquisitionService>().GetRecipesAsync();

        foreach (var id in new[] {local.Id, int.MaxValue})
        {
            var detail = await Candidates.GetAsync(id);
            Assert.AreEqual(0, detail.TotalCount);
            Assert.AreEqual(0, detail.Items.Count);
            Assert.AreEqual(JsonSerializer.Serialize(recipes), JsonSerializer.Serialize(detail.Recipes));
        }
    }

    [TestMethod]
    public async Task DerivedResourceLeadsUseTheExecutablePlatformReference_WithoutChangingStoredLinks()
    {
        var target = await Missing("Platform references");
        await Identity(target, ResourceSource.Steam, "123");
        await Identity(target, ResourceSource.DLsite, "RJ00000001");
        await Identity(target, ResourceSource.ExHentai, "456/abcdef");
        const string sharedPage = "https://example.invalid/post";
        await AddLead(target, AcquisitionLeadKind.SharedPage, sharedPage);
        var leadService = _sp.GetRequiredService<IAcquisitionLeadService>();
        var controller = new AcquisitionLeadController(leadService,
            _sp.GetRequiredService<IResourceSourceLinkService>(),
            _sp.GetRequiredService<IAcquisitionTorrentMetadataStore>(),
            _sp.GetRequiredService<IResourceService>());

        var response = await controller.GetAll(target);
        var derived = response.Data!.Where(l => l.IsDerived).ToList();
        Assert.AreEqual(3, derived.Count);
        foreach (var lead in derived)
        {
            Assert.IsTrue(FetchFromPlatformStep.TryReadLead(lead.Value, out var source, out var key));
            Assert.AreEqual(lead.SourceName, source.ToString());
            Assert.AreEqual($"{lead.SourceName}:{key}", lead.Value);
            Assert.AreEqual(0, lead.Id);
        }
        CollectionAssert.AreEquivalent(new[] {"Steam:123", "DLsite:RJ00000001", "ExHentai:456/abcdef"},
            derived.Select(l => l.Value).ToArray());
        Assert.AreEqual(sharedPage, response.Data!.Single(l => !l.IsDerived).Value);
        Assert.AreEqual(1, (await leadService.GetByResourceId(target)).Count);
    }

}
