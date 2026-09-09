using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Acquisition.Tests;

/// <summary>
/// The acquisition abstraction: steps, the item they pass along, and the four things a step can
/// say. Everything here runs against the in-memory host, which is the point — a step contract that
/// needs the workflow engine to be exercised would not be host-agnostic at all.
/// </summary>
[TestClass]
public sealed class AcquisitionStepContractTests
{
    private static AcquisitionWorkItem NewItem(string workingName = "A Work") => new()
    {
        ResourceId = 1,
        LeadKind = AcquisitionLeadKind.SharedPage,
        LeadValue = "https://example.com/thread/1",
        Title = "A Work",
        WorkingName = workingName,
        WorkingDirectory = "/tmp/acquisition/1"
    };

    private static InMemoryAcquisitionHost HostWith(params IAcquisitionStep[] steps)
    {
        var services = new ServiceCollection();

        foreach (var step in steps)
        {
            services.AddSingleton(step);
        }

        var provider = services.BuildServiceProvider();

        return new InMemoryAcquisitionHost(new AcquisitionStepRegistry(steps), provider);
    }

    private static AcquisitionRecipe RecipeOf(params IAcquisitionStep[] steps) =>
        new("test", steps.Select(s => new AcquisitionRecipeStep(s.Kind)).ToList());

    [TestMethod]
    public async Task ARunGoes_Continue_Suspend_Resume_Continue_Done()
    {
        var first = new RecordingStep("acquisition.test.first");
        var waiting = new SuspendingStep("acquisition.test.waiting", AcquisitionWaitReason.PasswordUnknown);
        var last = new RecordingStep("acquisition.test.last");
        var host = HostWith(first, waiting, last);

        var run = await host.StartAsync(RecipeOf(first, waiting, last), NewItem());

        Assert.AreEqual(AcquisitionRunState.Waiting, run.State);
        Assert.AreEqual(1, run.StepIndex, "the cursor stays on the step that suspended");
        Assert.AreEqual(AcquisitionWaitReason.PasswordUnknown, run.WaitReason);
        Assert.AreEqual("{\"asking\":\"PasswordUnknown\"}", run.WaitPromptJson);
        CollectionAssert.AreEqual(new[] { first.Kind, waiting.Kind }, run.ExecutedKinds);
        Assert.AreEqual(0, last.ExecuteCount, "nothing downstream may run while the run waits");

        var resumed = await host.ResumeAsync(run,
            new AcquisitionResumeSignal(AcquisitionWaitReason.PasswordUnknown, "\"hunter2\""));

        Assert.AreEqual(AcquisitionRunState.Completed, resumed.State);
        Assert.AreEqual(1, waiting.ResumeCount, "the signal goes to the step that asked for it");
        Assert.AreEqual(1, waiting.ExecuteCount, "and that step is not executed a second time");
        Assert.AreEqual(1, last.ExecuteCount);
        Assert.AreEqual("hunter2", resumed.Item.Variables["password"],
            "what the signal carried has to reach the rest of the run");
    }

    [TestMethod]
    public async Task Skip_IsNotAFailure_AndTheRunCarriesOn()
    {
        var first = new RecordingStep("acquisition.test.first");
        var skipping = new SkippingStep("acquisition.test.unpack", "there was no archive");
        var last = new RecordingStep("acquisition.test.last");
        var host = HostWith(first, skipping, last);

        var run = await host.StartAsync(RecipeOf(first, skipping, last), NewItem());

        Assert.AreEqual(AcquisitionRunState.Completed, run.State);
        Assert.AreEqual(1, last.ExecuteCount);
        CollectionAssert.AreEqual(new[] { ("acquisition.test.unpack", "there was no archive") },
            run.Skipped);
    }

    [TestMethod]
    public async Task Fail_StopsAtTheStepThatFailed()
    {
        var first = new RecordingStep("acquisition.test.first");
        var failing = new FailingStep("acquisition.test.fetch", "the link was dead");
        var last = new RecordingStep("acquisition.test.last");
        var host = HostWith(first, failing, last);

        var run = await host.StartAsync(RecipeOf(first, failing, last), NewItem());

        Assert.AreEqual(AcquisitionRunState.Failed, run.State);
        Assert.AreEqual("the link was dead", run.FailureMessage);
        Assert.AreEqual(1, run.StepIndex, "the cursor stays put so a retry re-runs the failed step");
        Assert.AreEqual(0, last.ExecuteCount);
    }

    [TestMethod]
    public async Task AMissingStepKind_FailsTheRunRatherThanSkippingIt()
    {
        var only = new RecordingStep("acquisition.test.first");
        var host = HostWith(only);
        var recipe = new AcquisitionRecipe("test",
            [new AcquisitionRecipeStep("acquisition.test.doesNotExist")]);

        var run = await host.StartAsync(recipe, NewItem());

        Assert.AreEqual(AcquisitionRunState.Failed, run.State);
        StringAssert.Contains(run.FailureMessage!, "acquisition.test.doesNotExist");
    }

    [TestMethod]
    public void TwoStepsClaimingOneKind_FailAtRegistration()
    {
        var a = new RecordingStep("acquisition.test.same");
        var b = new SkippingStep("acquisition.test.same", "whatever");

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            _ = new AcquisitionStepRegistry([a, b]));

        StringAssert.Contains(ex.Message, "acquisition.test.same");
    }

    [TestMethod]
    public void TheWorkItem_SurvivesASerializationRoundTrip()
    {
        var item = NewItem() with
        {
            CollectionId = 9,
            Links =
            [
                new AcquisitionLink("https://pan.example.com/s/abc", "1234", "pw",
                    AcquisitionDriveKind.Baidu)
            ],
            SelectedLinkIndex = 0,
            Files = ["/tmp/acquisition/1/a.zip"],
            ExtractedDirectory = "/tmp/acquisition/1/extracted",
            TargetDirectory = "/library/A Work",
            Variables = new Dictionary<string, string> { ["password"] = "pw" },
            Purchases = [new PurchaseRecord(3.5m, new DateTime(2026, 1, 2, 3, 4, 5), "SoulPlus")]
        };

        var json = JsonSerializer.Serialize(item);
        var round = JsonSerializer.Deserialize<AcquisitionWorkItem>(json)!;

        // The host writes this snapshot after every step and reads it back after a restart, so
        // anything lost here is lost from a real run. Compared as JSON rather than with
        // Assert.AreEqual: the record's generated equality compares its collection members by
        // reference, so two identical items never test equal anyway.
        Assert.AreEqual(json, JsonSerializer.Serialize(round));
        Assert.AreEqual("https://pan.example.com/s/abc", round.SelectedLink!.Url);
        Assert.AreEqual(AcquisitionDriveKind.Baidu, round.SelectedLink.DriveKind);
        Assert.AreEqual("pw", round.Variables["password"]);
        Assert.AreEqual(3.5m, round.Purchases.Single().Price);
    }

    [TestMethod]
    public void TheWorkItem_IsATextWorkpiece_SoTextTransformsApplyToTheDirectoryName()
    {
        var item = NewItem("[Group] A Work [1920x1080]");

        Assert.AreEqual("[Group] A Work [1920x1080]", ((ITextWorkpiece)item).WorkingText);

        var renamed = (AcquisitionWorkItem)((ITextWorkpiece)item).WithWorkingText("A Work");

        Assert.AreEqual("A Work", renamed.WorkingName);
        Assert.AreEqual(item.ResourceId, renamed.ResourceId, "a text transform must not change what the item is");
        Assert.AreEqual(item.Title, renamed.Title);
    }

    [TestMethod]
    public void EveryBuiltinRecipe_EndsByMaterializing()
    {
        Assert.AreEqual(5, BuiltinAcquisitionRecipes.All.Count);

        foreach (var recipe in BuiltinAcquisitionRecipes.All)
        {
            Assert.AreEqual(AcquisitionStepKinds.Materialize, recipe.Steps[^1].Kind,
                $"'{recipe.Name}' has to end with the resource actually pointing at its files");
        }
    }

    [TestMethod]
    public void EveryLeadKind_HasARecipeToRun()
    {
        foreach (var kind in Enum.GetValues<AcquisitionLeadKind>())
        {
            var name = BuiltinAcquisitionRecipes.DefaultRecipeNameFor(kind);

            Assert.IsNotNull(BuiltinAcquisitionRecipes.ByName(name),
                $"'{kind}' defaults to '{name}', which is not a built-in recipe");
        }
    }

    /// <summary>A step that does nothing but say it ran.</summary>
    private sealed class RecordingStep(string kind) : IAcquisitionStep
    {
        public string Kind => kind;
        public string DisplayName => kind;
        public Type? ConfigType => null;
        public int ExecuteCount { get; private set; }

        public Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
            AcquisitionWorkItem item, CancellationToken ct)
        {
            ExecuteCount++;

            return Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Continue(item));
        }
    }

    /// <summary>Suspends on its first run, then continues with whatever the signal carried.</summary>
    private sealed class SuspendingStep(string kind, AcquisitionWaitReason reason) : IAcquisitionStep
    {
        public string Kind => kind;
        public string DisplayName => kind;
        public Type? ConfigType => null;
        public int ExecuteCount { get; private set; }
        public int ResumeCount { get; private set; }

        public Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
            AcquisitionWorkItem item, CancellationToken ct)
        {
            ExecuteCount++;

            return Task.FromResult<AcquisitionStepOutcome>(
                new AcquisitionStepOutcome.Suspend(reason, $"{{\"asking\":\"{reason}\"}}", item));
        }

        public Task<AcquisitionStepOutcome> ResumeAsync(AcquisitionStepContext ctx,
            AcquisitionWorkItem item, AcquisitionResumeSignal signal, CancellationToken ct)
        {
            ResumeCount++;
            var answer = JsonSerializer.Deserialize<string>(signal.PayloadJson!)!;
            var variables = item.Variables.ToDictionary(x => x.Key, x => x.Value);

            variables["password"] = answer;

            return Task.FromResult<AcquisitionStepOutcome>(
                new AcquisitionStepOutcome.Continue(item with { Variables = variables }));
        }
    }

    private sealed class SkippingStep(string kind, string why) : IAcquisitionStep
    {
        public string Kind => kind;
        public string DisplayName => kind;
        public Type? ConfigType => null;

        public Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
            AcquisitionWorkItem item, CancellationToken ct) =>
            Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Skip(why, item));
    }

    private sealed class FailingStep(string kind, string message) : IAcquisitionStep
    {
        public string Kind => kind;
        public string DisplayName => kind;
        public Type? ConfigType => null;

        public Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
            AcquisitionWorkItem item, CancellationToken ct) =>
            Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Fail(message));
    }
}
