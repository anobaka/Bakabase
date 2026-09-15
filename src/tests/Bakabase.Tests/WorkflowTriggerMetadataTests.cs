using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowTriggerMetadataTests
{
    [TestMethod]
    public async Task EveryRegisteredFirstPartyTriggerDeclaresItsActivationSourceAndLocalizedExplanation()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        var registry = services.GetRequiredService<IWorkflowTriggerRegistry>();
        var descriptors = ActivatorUtilities.CreateInstance<WorkflowController>(services)
            .GetTriggers().Data!.ToDictionary(t => t.Kind);
        var expected = new Dictionary<string, (WorkflowActivationMode Mode, string Module)>
        {
            ["acquisition.requested"] = (WorkflowActivationMode.Module, "acquisition"),
            ["acquisition.statusChanged"] = (WorkflowActivationMode.SystemEvent, "acquisition"),
            ["downloader.resultReady"] = (WorkflowActivationMode.Module, "downloader"),
            ["downloader.completed"] = (WorkflowActivationMode.SystemEvent, "downloader"),
            ["postParser.manual"] = (WorkflowActivationMode.Manual, "postParser"),
            ["subscription.updated"] = (WorkflowActivationMode.SystemEvent, "subscription"),
            ["collection.membersAdded"] = (WorkflowActivationMode.SystemEvent, "collection"),
            ["resource.materialized"] = (WorkflowActivationMode.SystemEvent, "resource"),
            ["fs.manualScan"] = (WorkflowActivationMode.Manual, "fs"),
            ["fs.scheduledScan"] = (WorkflowActivationMode.Schedule, "fs"),
            ["fs.watch"] = (WorkflowActivationMode.Watch, "fs"),
        };
        CollectionAssert.AreEquivalent(expected.Keys.ToList(), descriptors.Keys.ToList(),
            "New registered triggers must be classified and included in the trigger catalog.");

        foreach (var trigger in registry.All)
        {
            var descriptor = descriptors[trigger.Kind];
            var contract = expected[trigger.Kind];
            Assert.IsNotNull(trigger.GetType().GetProperty(nameof(IWorkflowTrigger.ActivationMode)), trigger.Kind);
            Assert.IsNotNull(trigger.GetType().GetProperty(nameof(IWorkflowTrigger.SourceModule)), trigger.Kind);
            Assert.AreEqual(contract.Mode, descriptor.ActivationMode, trigger.Kind);
            Assert.AreEqual(contract.Module, descriptor.SourceModule, trigger.Kind);
            Assert.IsFalse(string.IsNullOrWhiteSpace(descriptor.Description), trigger.Kind);
            Assert.IsFalse(string.IsNullOrWhiteSpace(descriptor.DescriptionKey), trigger.Kind);
            StringAssert.StartsWith(descriptor.DescriptionKey, "workflow.trigger.");
            Assert.AreEqual(trigger.Description, descriptor.Description);
            Assert.AreEqual(trigger.DescriptionKey, descriptor.DescriptionKey);
            Assert.AreEqual(trigger.SupportsManualRun, descriptor.SupportsManualRun);
            if (descriptor.ActivationMode == WorkflowActivationMode.SystemEvent)
            {
                Assert.ThrowsException<JsonException>(() =>
                    trigger.Matches(Activator.CreateInstance(trigger.PayloadType)!, "{ invalid"),
                    $"{trigger.Kind} must report damaged filters to the event bus, not silently treat them as a nonmatch.");
            }
        }
        Assert.IsTrue(descriptors["downloader.completed"].SupportsManualRun,
            "Replay capability must not classify an event as an ordinary manual trigger.");
        Assert.IsFalse(descriptors["downloader.resultReady"].SupportsManualRun);
    }

    [TestMethod]
    public void OlderExtensionsRemainUsableWithoutInventingAnActivationSource()
    {
        IWorkflowTrigger trigger = new OlderExtensionTrigger();
        Assert.AreEqual(WorkflowActivationMode.Unknown, trigger.ActivationMode);
        Assert.AreEqual("", trigger.SourceModule);
        Assert.IsTrue(trigger.SupportsManualRun);
        Assert.IsNull(trigger.Description);
    }

    private sealed class OlderExtensionTrigger : IWorkflowTrigger
    {
        public string Kind => "extension.example";
        public string DisplayName => "An extension trigger";
        public Type PayloadType => typeof(object);
        public bool Matches(object payload, string? triggerFilterJson) => true;
        public IReadOnlyList<object> ExtractItems(object payload) => [payload];
        public string ResolveOutputItemType(string? triggerFilterJson) => "any";
    }
}
