using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Service.Components.Acquisition.Steps;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

/// <summary>
/// Reading a thread someone shared and getting links out of it. The model is faked — what is under
/// test is everything around it: which reader is chosen, what happens when part of the post costs
/// money, and whether the links, codes and identity survive the trip to the work item.
/// </summary>
[TestClass]
public sealed class SharedContentResolutionTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IServiceProvider _sp = null!;
    private readonly FakeLlm _llm = new();
    private readonly FakeSoulPlusReader _reader = new();
    private readonly FakePurchaser _purchaser = new();

    /// <summary>Answers with whatever the test set, so the step's own behavior is what is measured.</summary>
    private sealed class FakeLlm : ILlmService
    {
        public string ResponseText = "{}";
        public string? LastPrompt;

        public Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parametersOverride = null, CancellationToken ct = default)
        {
            LastPrompt = string.Join("\n", messages.Select(m => m.Text));

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ResponseText)));
        }

        public Task<ChatResponse> CompleteAsync(int providerConfigId, string modelId,
            IEnumerable<ChatMessage> messages, LlmModelParameters? parameters = null, AiFeature? feature = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature feature,
            IList<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Serves a fixture instead of the forum, and reports what is still locked.</summary>
    private sealed class FakeSoulPlusReader : ISharedContentReader
    {
        public string Fixture = "thread-with-two-drives.html";
        public List<SharedContentLock> Locks = [];
        public int Reads;

        public PostParserSource? Source => PostParserSource.SoulPlus;
        public int Priority => 100;

        public bool CanRead(string reference) => reference.Contains("soulplus", StringComparison.OrdinalIgnoreCase);

        public Task<PostContent> ReadAsync(string reference, CancellationToken ct)
        {
            Reads++;

            return Task.FromResult(new PostContent
            {
                Title = "A thread",
                MainHtml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Acquisition",
                    Fixture)),
                Locks = Locks.ToList(),
            });
        }
    }

    private sealed class FakePurchaser : ISharedContentPurchaser
    {
        public readonly List<string> Bought = [];

        public PostParserSource Source => PostParserSource.SoulPlus;

        public Task BuyAsync(string lockUrl, CancellationToken ct)
        {
            Bought.Add(lockUrl);

            return Task.CompletedTask;
        }
    }

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.RemoveAll<ILlmService>();
            services.AddSingleton<ILlmService>(_llm);
            services.RemoveAll<ISharedContentReader>();
            services.AddSingleton<ISharedContentReader>(_reader);
            services.AddSingleton<ISharedContentReader, PlainTextReader>();
            services.RemoveAll<ISharedContentPurchaser>();
            services.AddSingleton<ISharedContentPurchaser>(_purchaser);
        });

        _llm.ResponseText = """
            {
              "title": "Some Work",
              "resources": [
                {"link": "https://pan.baidu.com/s/1AbCdEfGhIj", "code": "8k2p", "password": "hunter2"},
                {"link": "https://mega.nz/folder/AbCdEfGh#ijklmnop", "code": null, "password": "hunter2"}
              ]
            }
            """;
    }

    private AcquisitionStepContext Context(string? configJson = null) => new(
        _sp, NullLogger.Instance, (_, _) => Task.CompletedTask, "/tmp/acquisition/1", configJson);

    private static AcquisitionWorkItem Item(int resourceId, string reference) => new()
    {
        ResourceId = resourceId,
        LeadKind = AcquisitionLeadKind.SharedPage,
        LeadValue = reference,
    };

    private async Task<int> CreateMissingResource(string name = "Something I do not have") =>
        (await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(name)).ResourceId;

    private ResolveSharedContentStep Step => new();

    [TestMethod]
    public async Task ReadsTheThread_AndPutsItsLinksCodesAndPasswordsOnTheItem()
    {
        var resourceId = await CreateMissingResource();

        var outcome = await Step.ExecuteAsync(Context(),
            Item(resourceId, "https://soulplus.example/thread/1"), CancellationToken.None);

        var item = ((AcquisitionStepOutcome.Continue) outcome).Item;

        Assert.AreEqual(2, item.Links.Count);
        Assert.AreEqual("8k2p", item.Links[0].AccessCode);
        Assert.AreEqual("hunter2", item.Links[0].ArchivePassword);
        Assert.AreEqual(AcquisitionDriveKind.Baidu, item.Links[0].DriveKind,
            "the drive is worked out from the host, not asked of the model");
        Assert.AreEqual(AcquisitionDriveKind.Mega, item.Links[1].DriveKind);
        Assert.AreEqual("Some Work", item.Title);
        Assert.AreEqual("Some Work", item.WorkingName, "which is also the folder name, unless something renames it");
    }

    /// <summary>
    /// A thread quoting a DLsite code is telling us what the work is. Recording it is what makes
    /// the same resource recognisable the next time it turns up somewhere else.
    /// </summary>
    [TestMethod]
    public async Task AnIdentityQuotedInTheThreadIsAttachedToTheResource()
    {
        var resourceId = await CreateMissingResource();

        await Step.ExecuteAsync(Context(), Item(resourceId, "https://soulplus.example/thread/1"),
            CancellationToken.None);

        var links = await _sp.GetRequiredService<IResourceSourceLinkService>().GetByResourceId(resourceId);
        var dlsite = links.SingleOrDefault(l => l.Source == ResourceSource.DLsite);

        Assert.IsNotNull(dlsite, "RJ012345 is in the fixture's first line");
        Assert.AreEqual("RJ012345", dlsite!.SourceKey);
    }

    /// <summary>
    /// Spending money is the one thing in this pipeline that must never happen quietly. Above the
    /// limit the run stops and shows the price; approving it buys and carries on.
    /// </summary>
    [TestMethod]
    public async Task PaidContentAboveTheLimitSuspends_AndApprovingItBuysAndContinues()
    {
        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.AutoPurchaseLimit = 5m;
        _reader.Locks = [new SharedContentLock("https://soulplus.example/buy/1", 20m, false)];

        var resourceId = await CreateMissingResource();
        var item = Item(resourceId, "https://soulplus.example/thread/1");

        var outcome = await Step.ExecuteAsync(Context(), item, CancellationToken.None);
        var suspended = (AcquisitionStepOutcome.Suspend) outcome;

        Assert.AreEqual(AcquisitionWaitReason.PaidContent, suspended.Reason);
        Assert.AreEqual(0, _purchaser.Bought.Count, "nothing is bought before someone says so");

        var prompt = JsonSerializer.Deserialize<ResolveSharedContentStep.PurchasePrompt>(
            suspended.PromptJson!, Json)!;

        Assert.AreEqual(20m, prompt.Locked.Single().Price, "the user is shown what it costs");
        Assert.AreEqual(5m, prompt.Limit);

        var resumed = await Step.ResumeAsync(Context(), item,
            new AcquisitionResumeSignal(AcquisitionWaitReason.PaidContent,
                JsonSerializer.Serialize(new ResolveSharedContentStep.PurchaseSignal(true), Json)),
            CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(resumed);
        CollectionAssert.AreEqual(new[] {"https://soulplus.example/buy/1"}, _purchaser.Bought);
    }

    [TestMethod]
    public async Task PaidContentWithinTheLimitIsBoughtWithoutAsking()
    {
        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.AutoPurchaseLimit = 30m;
        _reader.Locks = [new SharedContentLock("https://soulplus.example/buy/1", 20m, false)];

        var outcome = await Step.ExecuteAsync(Context(),
            Item(await CreateMissingResource(), "https://soulplus.example/thread/1"), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        Assert.AreEqual(1, _purchaser.Bought.Count);
        Assert.AreEqual(2, _reader.Reads, "the post is read again so the unlocked part is included");
    }

    /// <summary>
    /// A lock with no stated price used to compare as free and buy itself. It is now treated as
    /// something to ask about, whatever the limit is.
    /// </summary>
    [TestMethod]
    public async Task ALockWithNoStatedPriceIsNeverBoughtUnattended()
    {
        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.AutoPurchaseLimit = 1000m;
        _reader.Locks = [new SharedContentLock("https://soulplus.example/buy/1", null, false)];

        var outcome = await Step.ExecuteAsync(Context(),
            Item(await CreateMissingResource(), "https://soulplus.example/thread/1"), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Suspend>(outcome);
        Assert.AreEqual(0, _purchaser.Bought.Count);
    }

    [TestMethod]
    public async Task NeverBuy_OverridesTheGlobalLimit()
    {
        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.AutoPurchaseLimit = 1000m;
        _reader.Locks = [new SharedContentLock("https://soulplus.example/buy/1", 1m, false)];

        var outcome = await Step.ExecuteAsync(Context("""{"neverBuy":true}"""),
            Item(await CreateMissingResource(), "https://soulplus.example/thread/1"), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Suspend>(outcome);
        Assert.AreEqual(0, _purchaser.Bought.Count);
    }

    /// <summary>
    /// Sharing is not only forums. A block of pasted text is content too, and the reader that takes
    /// anything is the one that gets it.
    /// </summary>
    [TestMethod]
    public async Task PastedTextIsReadByTheFallbackReader()
    {
        _llm.ResponseText = """
            {"title":"Pasted","resources":[{"link":"https://mega.nz/folder/x","code":null,"password":null}]}
            """;

        var outcome = await Step.ExecuteAsync(Context(),
            Item(await CreateMissingResource(), "Here is the file: https://mega.nz/folder/x"),
            CancellationToken.None);

        var item = ((AcquisitionStepOutcome.Continue) outcome).Item;

        Assert.AreEqual("https://mega.nz/folder/x", item.Links.Single().Url);
    }

    [TestMethod]
    public async Task NothingReadableFailsWithSomethingTheUserCanActOn()
    {
        var outcome = await Step.ExecuteAsync(Context(),
            Item(await CreateMissingResource(), "   "), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(outcome);
    }

    [TestMethod]
    public void TheReadersAreOrderedSoASiteAwareOneAlwaysWins()
    {
        var resolver = new SharedContentReaderResolver([new PlainTextReader(), _reader]);

        Assert.AreSame(_reader, resolver.Resolve("https://soulplus.example/thread/1"));
        Assert.IsInstanceOfType<PlainTextReader>(resolver.Resolve("some pasted text with a link"));
        Assert.IsNull(resolver.Resolve("short"), "a stray word is not shared content");
    }
}
