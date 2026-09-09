using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Platform;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Steps;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

/// <summary>
/// Getting something from a platform the user already holds it on.
/// <para>
/// The other half of acquisition: no forum post, no cloud drive, no person clicking download — the
/// platform has a way of handing the files over, and each platform's way is its own. What the step
/// knows is only that a fetch either finishes or is under way.
/// </para>
/// </summary>
[TestClass]
public sealed class PlatformFetchTests
{
    private IServiceProvider _sp = null!;
    private string _root = null!;

    /// <summary>A platform whose answers the test decides.</summary>
    private sealed class FakePlatform : IPlatformConnector
    {
        public static PlatformFetchOutcome Answer = new PlatformFetchOutcome.Started("working on it");

        public static string? Local;

        public static int Fetches;

        public ResourceSource Source => ResourceSource.Steam;
        public bool CanFetch => true;

        public Task<IReadOnlyList<PlatformHolding>> EnumerateHoldingsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PlatformHolding>>([]);

        public Task<PlatformFetchOutcome> FetchAsync(string sourceKey, string workDirectory,
            Func<int, string?, Task>? onProgress, CancellationToken ct)
        {
            Fetches++;

            return Task.FromResult(Answer);
        }

        public Task<string?> DetectLocalPathAsync(string sourceKey, CancellationToken ct) =>
            Task.FromResult(Local);
    }

    [TestInitialize]
    public async Task Setup()
    {
        FakePlatform.Answer = new PlatformFetchOutcome.Started("working on it");
        FakePlatform.Local = null;
        FakePlatform.Fetches = 0;

        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
            services.AddKeyedScoped<IPlatformConnector, FakePlatform>(ResourceSource.Steam));
        _root = Path.Combine(Path.GetTempPath(), $"BakabasePlatform_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, true); }
        catch { /* best effort */ }
    }

    private AcquisitionStepContext Context() =>
        new(_sp, NullLogger.Instance, (_, _) => Task.CompletedTask, _root);

    private static AcquisitionWorkItem Item(string leadValue = "Steam:220") => new()
    {
        ResourceId = 1,
        LeadKind = AcquisitionLeadKind.PlatformHolding,
        LeadValue = leadValue,
        WorkingDirectory = "",
    };

    private static FetchFromPlatformStep Step() => new();

    /// <summary>
    /// A platform that hands the files over straight away — a DLsite purchase — finishes in one
    /// go, and says where the files are rather than pretending they are in the run's directory.
    /// </summary>
    [TestMethod]
    public async Task AFetchThatFinishesCarriesOnWithWhereTheFilesAre()
    {
        var placed = Path.Combine(_root, "Half-Life");

        Directory.CreateDirectory(placed);
        FakePlatform.Answer = new PlatformFetchOutcome.Done(placed);

        var outcome = await Step().ExecuteAsync(Context(), Item(), CancellationToken.None);

        var carried = (AcquisitionStepOutcome.Continue) outcome;

        Assert.AreEqual(placed, carried.Item.ExtractedDirectory);
        Assert.AreEqual("Half-Life", carried.Item.WorkingName,
            "the name comes from where the platform put it, so placement has something to call it");
    }

    /// <summary>
    /// Steam installs on its own schedule and tells nobody. The run waits rather than blocking a
    /// worker for an hour, which is the whole reason suspension exists.
    /// </summary>
    [TestMethod]
    public async Task AFetchThatIsUnderWayWaits()
    {
        var outcome = await Step().ExecuteAsync(Context(), Item(), CancellationToken.None);

        var waiting = (AcquisitionStepOutcome.Suspend) outcome;

        Assert.AreEqual(AcquisitionWaitReason.PlatformFetch, waiting.Reason);
        StringAssert.Contains(waiting.PromptJson!, "working on it");
    }

    /// <summary>
    /// Already here — from an earlier run, or because the user installed it themselves while this
    /// was queued. Steps re-run after a restart, so noticing is not optional.
    /// </summary>
    [TestMethod]
    public async Task SomethingAlreadyOnTheMachineIsNotFetchedAgain()
    {
        FakePlatform.Local = Path.Combine(_root, "already");
        Directory.CreateDirectory(FakePlatform.Local);

        var outcome = await Step().ExecuteAsync(Context(), Item(), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        Assert.AreEqual(0, FakePlatform.Fetches, "the platform was never asked");
    }

    /// <summary>
    /// The watcher checks on a timer, so a resume can arrive before the files do. Going back to
    /// waiting is right; failing would throw away a fetch that is perfectly fine.
    /// </summary>
    [TestMethod]
    public async Task BeingWokenTooEarlyGoesBackToWaiting()
    {
        var outcome = await Step().ResumeAsync(Context(), Item(),
            new AcquisitionResumeSignal(AcquisitionWaitReason.PlatformFetch, "{}"),
            CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Suspend>(outcome);

        FakePlatform.Local = Path.Combine(_root, "arrived");
        Directory.CreateDirectory(FakePlatform.Local);

        var second = await Step().ResumeAsync(Context(), Item(),
            new AcquisitionResumeSignal(AcquisitionWaitReason.PlatformFetch, "{}"),
            CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(second);
    }

    [TestMethod]
    public async Task ARunThatIsNotAPlatformFetchSkipsTheStep()
    {
        var outcome = await Step().ExecuteAsync(Context(),
            Item() with {LeadKind = AcquisitionLeadKind.SharedPage},
            CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Skip>(outcome);
    }

    /// <summary>
    /// A catalog names works but holds none of them. Asking it to hand one over fails with that
    /// said out loud, rather than hanging on a wait nothing will ever answer.
    /// </summary>
    [TestMethod]
    public async Task APlatformNothingSpeaksForFails()
    {
        var outcome = await Step().ExecuteAsync(Context(), Item("Bangumi:12345"), CancellationToken.None);

        var failed = (AcquisitionStepOutcome.Fail) outcome;

        StringAssert.Contains(failed.Message, "Bangumi");
    }

    /// <summary>
    /// A gallery's key has a slash in it and a Steam app's is a number, so only the first colon
    /// separates the platform from the key.
    /// </summary>
    [TestMethod]
    public void APlatformLeadIsReadTheWayASourceLinkIsWritten()
    {
        Assert.IsTrue(FetchFromPlatformStep.TryReadLead("ExHentai:12345/abcdef0123",
            out var source, out var key));
        Assert.AreEqual(ResourceSource.ExHentai, source);
        Assert.AreEqual("12345/abcdef0123", key);

        Assert.IsFalse(FetchFromPlatformStep.TryReadLead("no-colon", out _, out _));
        Assert.IsFalse(FetchFromPlatformStep.TryReadLead("NotAPlatform:1", out _, out _));
        Assert.IsFalse(FetchFromPlatformStep.TryReadLead("Steam:", out _, out _));
    }

    /// <summary>
    /// The three platforms are registered by name, and a connector is built only when its platform
    /// is the one being asked about. Enumerating them would construct an HTTP session, a download
    /// queue and a shop client every time a step ran.
    /// </summary>
    [TestMethod]
    public void EachPlatformIsFoundByNameWithoutBuildingTheOthers()
    {
        var registry = _sp.GetRequiredService<IPlatformConnectorRegistry>();

        CollectionAssert.AreEquivalent(
            new[] {ResourceSource.DLsite, ResourceSource.Steam, ResourceSource.ExHentai},
            registry.Sources.ToArray());

        Assert.AreEqual(ResourceSource.Steam, registry.Get(ResourceSource.Steam)!.Source,
            "the fake stands in for Steam here");
        Assert.IsNull(registry.Get(ResourceSource.Bangumi),
            "a catalog holds nothing, so nothing speaks for it");
    }
}
