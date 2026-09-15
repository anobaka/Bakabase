using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Workflow;
using Bakabase.Service.Components.Workflow.Resources;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// A resource gaining its files is an event worth building on — "when that DLsite work I bought
/// finally lands, tell me" is a workflow, not a feature. These tests cover the trigger end of it:
/// that materializing publishes the event, that a definition watching it gets a run carrying enough
/// to act on, and that the source filter narrows to the platform the user cared about.
/// </summary>
[TestClass]
public sealed class ResourceMaterializedWorkflowTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"ResourceMaterializedWorkflowTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    private IWorkflowDefinitionService Workflows => _sp.GetRequiredService<IWorkflowDefinitionService>();

    private Task<Modules.Workflow.Abstractions.Models.Domain.WorkflowDefinition> AddDefinition(
        string? filterJson = null) =>
        Workflows.CreateAsync(new WorkflowDefinitionCreationInputModel
        {
            Name = $"on-materialized-{Guid.NewGuid():N}",
            TriggerKind = ResourceWorkflowKinds.TriggerMaterialized,
            TriggerFilterJson = filterJson,
            Enabled = true
        });

    private async Task<List<Modules.Workflow.Abstractions.Models.Domain.WorkflowRun>> RunsOf(int definitionId) =>
        (await Workflows.SearchRunsAsync(new WorkflowRunSearchInputModel
            { WorkflowDefinitionId = definitionId })).Data ?? [];

    /// <summary>
    /// A resource with an identity but no files — the shape everything in this milestone is about.
    /// </summary>
    private async Task<Resource> AddUnmaterializedResource(string name, ResourceSource source,
        string sourceKey)
    {
        var resource = new Resource
        {
            Path = null,
            IsFile = false,
            Status = ResourceStatus.Active,
            DisplayName = name,
            FileCreatedAt = DateTime.Now,
            FileModifiedAt = DateTime.Now,
            SourceLinks = [new ResourceSourceLink { Source = source, SourceKey = sourceKey }]
        };
        await _sp.GetRequiredService<IResourceService>().AddOrPutRange([resource]);

        await _sp.GetRequiredService<IReservedPropertyValueService>().Add(new ReservedPropertyValue
        {
            ResourceId = resource.Id,
            Scope = (int)PropertyValueScope.Synchronization,
            Name = name
        });

        return resource;
    }

    private string MakeDirectory(string name)
    {
        var path = Path.Combine(_testRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [TestMethod]
    public async Task Materializing_StartsARunCarryingTheResourceName()
    {
        var definition = await AddDefinition();
        var resource = await AddUnmaterializedResource("A Bought Work", ResourceSource.DLsite, "RJ00000123");
        var path = MakeDirectory("bought-work");

        await _sp.GetRequiredService<IResourceMaterializationService>()
            .MaterializeAsync(resource.Id, path,
                new MaterializationOptions(EnqueuePathMarkSync: false), CancellationToken.None);

        var runs = await RunsOf(definition.Id);
        Assert.AreEqual(1, runs.Count, "the workflow was watching for exactly this");

        var payload = JsonSerializer.Deserialize<ResourceMaterializedPayload>(runs[0].PayloadJson!,
            WorkflowJson.Options);
        Assert.IsNotNull(payload);
        Assert.AreEqual(resource.Id, payload!.ResourceId);
        Assert.AreEqual("A Bought Work", payload.Name,
            "a notification saying 'a resource landed' without saying which is useless");
        Assert.AreEqual(path.StandardizePath(), payload.Path);
        Assert.AreEqual(ResourceSource.DLsite, payload.SourceLinks.Single().Source);
        Assert.AreEqual("RJ00000123", payload.SourceLinks.Single().SourceKey);
    }

    [TestMethod]
    public async Task Dematerializing_StartsNothing()
    {
        var definition = await AddDefinition();
        var resource = await AddUnmaterializedResource("A Work", ResourceSource.DLsite, "RJ00000200");
        var materialization = _sp.GetRequiredService<IResourceMaterializationService>();
        var noMarkSync = new MaterializationOptions(EnqueuePathMarkSync: false);

        await materialization.MaterializeAsync(resource.Id, MakeDirectory("work"), noMarkSync,
            CancellationToken.None);
        await materialization.DematerializeAsync(resource.Id, CancellationToken.None);

        Assert.AreEqual(1, (await RunsOf(definition.Id)).Count,
            "losing its files is not the event workflows subscribed to");
    }

    [TestMethod]
    public async Task TheSourceFilter_NarrowsToThePlatformTheUserCaredAbout()
    {
        var dlsiteOnly = await AddDefinition(
            JsonSerializer.Serialize(new { sources = new[] { (int)ResourceSource.DLsite } }));
        var steamOnly = await AddDefinition(
            JsonSerializer.Serialize(new { sources = new[] { (int)ResourceSource.Steam } }));

        var resource = await AddUnmaterializedResource("A Bought Work", ResourceSource.DLsite, "RJ00000300");

        await _sp.GetRequiredService<IResourceMaterializationService>()
            .MaterializeAsync(resource.Id, MakeDirectory("filtered"),
                new MaterializationOptions(EnqueuePathMarkSync: false), CancellationToken.None);

        Assert.AreEqual(1, (await RunsOf(dlsiteOnly.Id)).Count);
        Assert.AreEqual(0, (await RunsOf(steamOnly.Id)).Count,
            "a Steam-only workflow has no business running for a DLsite work");
    }

    [TestMethod]
    public void TheItem_AnswersWithWhatAnActivityNeeds()
    {
        var trigger = new ResourceMaterializedTrigger();
        var payload = new ResourceMaterializedPayload
        {
            ResourceId = 7,
            Name = "A Work",
            Path = "/library/games/A Work",
            SourceLinks = [new ResourceWorkflowSourceLink(ResourceSource.DLsite, "RJ00000123")]
        };

        var items = trigger.ExtractItems(payload);
        Assert.AreEqual(1, items.Count, "one materialization is one resource");
        Assert.AreEqual(WorkflowItemTypes.Resource, trigger.ResolveOutputItemType(null));

        var item = (ResourceWorkflowItem)items[0];
        var variables = item.GetWorkflowSystemVariables();
        Assert.AreEqual("A Work", variables["name"]);
        Assert.AreEqual("/library/games/A Work", variables["path"]);
        Assert.AreEqual("A Work", variables["directoryName"]);
        Assert.AreEqual("true", variables["hasLocalPath"]);
        Assert.AreEqual("DLsite", variables["sources"]);
    }
}
