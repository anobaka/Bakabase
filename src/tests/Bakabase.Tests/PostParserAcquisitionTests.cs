using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Acquisition.Models.Input;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition;
using Bakabase.Service.Components.Acquisition.Steps;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostParserAcquisitionTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IServiceProvider _sp = null!;
    private BakabaseDbContext Db => _sp.GetRequiredService<BakabaseDbContext>();
    private IAcquisitionLeadService Leads => _sp.GetRequiredService<IAcquisitionLeadService>();
    private IPlaceholderResourceService Placeholders => _sp.GetRequiredService<IPlaceholderResourceService>();
    private PostParserAcquisitionService Importer => ActivatorUtilities.CreateInstance<PostParserAcquisitionService>(_sp);

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.RemoveAll<IPostContentService>();
            services.RemoveAll<IPostDownloadInfoExtractor>();
            services.AddSingleton<IPostContentService, UnexpectedReader>();
            services.AddSingleton<IPostDownloadInfoExtractor, UnexpectedExtractor>();
        });
    }

    private async Task<PostParserTaskDbModel> ParsedPost(IReadOnlyList<PostDownloadResource> links,
        string? text = null)
    {
        var post = new PostParserTaskDbModel
        {
            Source = PostParserSource.SoulPlus,
            Link = "https://soulplus.example/thread/test",
            Text = text,
            Title = "Parsed resource",
            Revision = 7,
            Results = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                [nameof(PostParseTarget.DownloadInfo)] = new {resources = links}
            }, Json)
        };
        Db.Set<PostParserTaskDbModel>().Add(post);
        await Db.SaveChangesAsync();
        return post;
    }

    private static PostDownloadResource Link(string url, string? code = null, string? password = null) =>
        new() {Link = url, Code = code, Password = password};

    private Task<PostParserAcquisitionResult> Import(PostParserTaskDbModel post, params int[] indices) =>
        Importer.ImportAsync(post.Id, post.Revision, null, indices, CancellationToken.None);

    private async Task<(int Resources, int Leads, int Acquisitions, int Runs)> Counts() =>
        (await Db.Set<ResourceDbModel>().CountAsync(), await Db.Set<AcquisitionLeadDbModel>().CountAsync(),
            await Db.Set<AcquisitionTaskDbModel>().CountAsync(), await Db.Set<WorkflowRunDbModel>().CountAsync());

    [TestMethod]
    public async Task ImportPreservesAllSelectedLinksAndCredentialsWithoutStartingADownload()
    {
        var post = await ParsedPost([
            Link("https://pan.baidu.com/s/cloud", "abcd", "cloud archive"),
            Link("https://files.example/archive.zip", "direct-code", "direct archive"),
            Link("magnet:?xt=urn:btih:123456", null, "torrent archive"),
            Link("https://example.com/not-selected", "unused", "unused")
        ]);
        var result = await Import(post, 0, 1, 2);

        Assert.IsTrue(result.Created);
        Assert.AreEqual(3, result.LeadCount);
        var saved = (await Leads.GetByResourceId(result.ResourceId)).ToDictionary(l => l.Value);
        Assert.AreEqual(3, saved.Count);
        var cloud = saved["https://pan.baidu.com/s/cloud"];
        Assert.AreEqual("abcd", cloud.AccessCode);
        Assert.AreEqual("cloud archive", cloud.Password);
        Assert.AreEqual(AcquisitionLeadKind.SharedPage, cloud.Kind);
        Assert.AreEqual("direct-code", saved["https://files.example/archive.zip"].AccessCode);
        Assert.AreEqual("direct archive", saved["https://files.example/archive.zip"].Password);
        Assert.AreEqual(AcquisitionLeadKind.DirectUrl, saved["https://files.example/archive.zip"].Kind);
        Assert.AreEqual("torrent archive", saved["magnet:?xt=urn:btih:123456"].Password);
        Assert.AreEqual(AcquisitionLeadKind.Magnet, saved["magnet:?xt=urn:btih:123456"].Kind);
        Assert.IsTrue(saved.Values.All(l => l.SourceReference == post.Link));
        Assert.IsTrue(saved.Values.All(l => l.Origin == AcquisitionLeadOrigin.PostParser));
        Assert.IsTrue(saved.Values.All(l => l.IsResolved));
        Assert.IsFalse((await _sp.GetRequiredService<IResourceService>().Get(result.ResourceId))!.HasLocalPath);
        Assert.AreEqual(0, await Db.Set<AcquisitionTaskDbModel>().CountAsync());
        Assert.AreEqual(0, await Db.Set<WorkflowRunDbModel>().CountAsync());
    }

    [TestMethod]
    public async Task RepeatedIndicesUrlSpellingsAndRepeatedImportsDoNotDuplicateResourcesOrLeads()
    {
        var post = await ParsedPost([
            Link("https://Example.COM/Archive.zip", "code", "password"),
            Link("https://example.com/Archive.zip", "code", "password")
        ]);
        var first = await Import(post, 0, 1, 0);
        var before = await Counts();
        var second = await Import(post, 1, 0, 1);
        Assert.AreEqual(first.ResourceId, second.ResourceId);
        Assert.IsFalse(second.Created);
        Assert.AreEqual(1, first.LeadCount);
        Assert.AreEqual(1, second.LeadCount);
        Assert.AreEqual(before, await Counts());
    }

    [TestMethod]
    public async Task StaleRevisionIsRejectedBeforeAnyResourceOrLeadIsWritten()
    {
        var post = await ParsedPost([Link("https://example.com/archive.zip")]);
        var before = await Counts();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Importer.ImportAsync(post.Id, post.Revision - 1, null, [0], CancellationToken.None));
        Assert.AreEqual(before, await Counts());
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    public async Task InvalidSelectionIsRejectedBeforeWritingAnything(int index)
    {
        var post = await ParsedPost([Link("https://example.com/archive.zip")]);
        var before = await Counts();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Import(post, index));
        Assert.AreEqual(before, await Counts());
    }

    [TestMethod]
    public async Task EmptySelectionAndInvalidLinksCannotCreatePlaceholderResources()
    {
        var post = await ParsedPost([Link("https://example.com/archive.zip"), Link("javascript:alert(1)")]);
        var before = await Counts();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Import(post));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Import(post, 0, 1));
        Assert.AreEqual(before, await Counts());
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("{\"DownloadInfo\":null}")]
    [DataRow("{\"DownloadInfo\":[]}")]
    [DataRow("{\"1\":\"invalid\"}")]
    [DataRow("{\"DownloadInfo\":{\"data\":null}}")]
    [DataRow("{\"1\":{\"Data\":[]}}")]
    [DataRow("{\"DownloadInfo\":{\"resources\":{}}}")]
    [DataRow("{\"DownloadInfo\":{\"resources\":[null]}}")]
    [DataRow("{\"DownloadInfo\":{\"resources\":[{\"link\":42}]}}")]
    public async Task MalformedLegacyResultsRequireReparsingWithoutWritingResourcesOrLeads(string results)
    {
        var post = await ParsedPost([Link("https://example.com/archive.zip")]);
        post.Results = results;
        await Db.SaveChangesAsync();
        var before = await Counts();
        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() => Import(post, 0));
        StringAssert.Contains(error.Message, "Parse the post again");
        Assert.AreEqual(before, await Counts());
    }

    [TestMethod]
    public async Task LinksOwnedByDifferentResourcesAreNotMergedOrPartiallyImported()
    {
        var first = await Placeholders.CreateByTitle("First existing resource");
        var second = await Placeholders.CreateByTitle("Second existing resource");
        await Leads.Add(first.ResourceId, new AcquisitionLeadAddInputModel
        {
            Kind = AcquisitionLeadKind.SharedPage, Value = "https://example.com/first",
            Origin = AcquisitionLeadOrigin.User, AccessCode = "original"
        });
        await Leads.Add(second.ResourceId, new AcquisitionLeadAddInputModel
        {
            Kind = AcquisitionLeadKind.DirectUrl, Value = "https://example.com/second.zip",
            Origin = AcquisitionLeadOrigin.User
        });
        var post = await ParsedPost([
            Link("https://example.com/new.zip", "new-code"),
            Link("https://example.com/first", "original", "new password"),
            Link("https://example.com/second.zip")
        ]);
        var before = await Counts();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Import(post, 0, 1, 2));
        Assert.AreEqual(before, await Counts());
        Assert.IsNull((await Leads.GetByResourceId(first.ResourceId)).Single().Password);
        Assert.IsNull(await Leads.FindByValue(AcquisitionLeadKind.DirectUrl, "https://example.com/new.zip"));
    }

    [TestMethod]
    public async Task DuplicateLinksWithDifferentCredentialsRequireAnExplicitChoice()
    {
        var post = await ParsedPost([
            Link("https://example.com/archive.zip", "first", "password one"),
            Link("https://EXAMPLE.com/archive.zip", "second", "password two")
        ]);
        var before = await Counts();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Import(post, 0, 1));
        Assert.AreEqual(before, await Counts());
    }

    [TestMethod]
    [DataRow("new code", "original password")]
    [DataRow("original code", "new password")]
    public async Task ConflictsWithSavedCredentialsAreRejectedWithoutPartiallyImportingOtherLinks(
        string code, string password)
    {
        var resource = await Placeholders.CreateByTitle("Existing resource");
        await Leads.Add(resource.ResourceId, new AcquisitionLeadAddInputModel
        {
            Kind = AcquisitionLeadKind.SharedPage, Value = "https://example.com/existing",
            Origin = AcquisitionLeadOrigin.User, AccessCode = "original code", Password = "original password"
        });
        var post = await ParsedPost([
            Link("https://example.com/new.zip"), Link("https://example.com/existing", code, password)
        ]);
        var before = await Counts();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Import(post, 0, 1));
        Assert.AreEqual(before, await Counts());
        var existing = (await Leads.GetByResourceId(resource.ResourceId)).Single();
        Assert.AreEqual("original code", existing.AccessCode);
        Assert.AreEqual("original password", existing.Password);
        Assert.IsFalse(existing.IsResolved);
    }

    [TestMethod]
    public async Task CachedSharedPageStartsWithoutAiConfigurationAndIsNotReadOrExtractedAgain()
    {
        var post = await ParsedPost([Link("https://pan.baidu.com/s/shared", "abcd", "archive password")]);
        var imported = await Import(post, 0);
        var lead = (await Leads.GetByResourceId(imported.ResourceId)).Single();
        var payload = await StartWithCachedLead(lead, AcquisitionStepKinds.ResolveSharedContent);
        await AssertCachedResolution(payload, "abcd", "archive password");
    }

    [TestMethod]
    public async Task CandidateUsesResolvedLeadValidationEvenWhenTheGenericWorkflowNeedsAi()
    {
        var post = await ParsedPost([Link("https://pan.baidu.com/s/shared", "abcd", "archive password")]);
        var imported = await Import(post, 0);
        var options = _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PostParserCandidate_" + Guid.NewGuid());
        options.LibraryRootDirectory = System.IO.Path.Combine(directory, "library");
        options.InboxDirectory = System.IO.Path.Combine(directory, "inbox");
        await _sp.GetRequiredService<IAiFeatureService>().DeleteConfigAsync(AiFeature.PostParser);
        await _sp.GetRequiredService<IAiFeatureService>().DeleteConfigAsync(AiFeature.Default);
        await _sp.GetRequiredService<AcquisitionRecipeSeeder<BakabaseDbContext>>().SeedAsync();

        var page = await _sp.GetRequiredService<AcquisitionCandidateService>().GetAsync(imported.ResourceId);
        var recipe = page.Recipes.Single(r => r.Name == BuiltinAcquisitionRecipes.ForumPostWithCloudDrive);
        Assert.IsTrue(recipe.Validation.Diagnostics.Any(d => d.Code == "acquisition.ai.missing"));
        var route = page.Items.Single().Leads.Single();
        Assert.IsTrue(route.RecipeValidations.TryGetValue(recipe.DefinitionId, out var resolvedValidation));
        Assert.IsFalse(resolvedValidation!.Diagnostics.Any(d => d.Code == "acquisition.ai.missing"));
        Assert.IsTrue(resolvedValidation.IsValid);
        Assert.AreEqual(0, await Db.Set<WorkflowRunDbModel>().CountAsync());
    }

    [TestMethod]
    public async Task PastedResultEnrichingAnExistingUserLeadAlsoRemainsUsableWithoutAi()
    {
        var resource = await Placeholders.CreateByTitle("Existing resource");
        await Leads.Add(resource.ResourceId, new AcquisitionLeadAddInputModel
        {
            Kind = AcquisitionLeadKind.SharedPage, Value = "https://pan.baidu.com/s/existing",
            Origin = AcquisitionLeadOrigin.User
        });
        var post = await ParsedPost([Link("https://pan.baidu.com/s/existing", "abcd", "archive password")],
            "Pasted post with a cloud drive link");
        var imported = await Import(post, 0);
        Assert.AreEqual(resource.ResourceId, imported.ResourceId);
        Assert.IsFalse(imported.Created);
        var lead = (await Leads.GetByResourceId(imported.ResourceId)).Single();
        Assert.IsTrue(lead.IsResolved);
        Assert.AreEqual(AcquisitionLeadOrigin.User, lead.Origin, "Enriching a lead preserves who added it.");
        Assert.IsNull(lead.SourceReference, "Pasted text does not pretend to be a source URL.");
        var payload = await StartWithCachedLead(lead, AcquisitionStepKinds.ResolveSharedContent);
        await AssertCachedResolution(payload, "abcd", "archive password");
    }

    [TestMethod]
    public async Task UnresolvedSharedPageStillRequiresAiBeforeCreatingATask()
    {
        var resource = await Placeholders.CreateByTitle("Unparsed resource");
        var added = await Leads.Add(resource.ResourceId, new AcquisitionLeadAddInputModel
        {
            Kind = AcquisitionLeadKind.SharedPage, Value = "https://example.com/unparsed",
            Origin = AcquisitionLeadOrigin.User
        });
        await Assert.ThrowsExactlyAsync<Bakabase.Modules.Workflow.Abstractions.Components.WorkflowValidationException>(
            () => StartWithCachedLead(added.Lead!, AcquisitionStepKinds.ResolveSharedContent));
        Assert.AreEqual(0, await Db.Set<AcquisitionTaskDbModel>().CountAsync());
        Assert.AreEqual(0, await Db.Set<WorkflowRunDbModel>().CountAsync());
    }

    [TestMethod]
    [DataRow("https://files.example/download.zip", AcquisitionLeadKind.DirectUrl, AcquisitionDriveKind.DirectUrl)]
    [DataRow("magnet:?xt=urn:btih:123456", AcquisitionLeadKind.Magnet, AcquisitionDriveKind.Magnet)]
    public async Task DirectAndMagnetMetadataSurvivesPersistenceAndWorkflowTriggerExtraction(
        string url, AcquisitionLeadKind kind, AcquisitionDriveKind drive)
    {
        var post = await ParsedPost([Link(url, "access", "archive password")]);
        var imported = await Import(post, 0);
        var lead = (await Leads.GetByResourceId(imported.ResourceId)).Single();
        Assert.AreEqual(kind, lead.Kind);
        var payload = await StartWithCachedLead(lead, AcquisitionStepKinds.SelectLink);
        var item = (AcquisitionWorkItem) new AcquisitionRequestedTrigger().ExtractItems(payload).Single();
        Assert.AreEqual(url, item.Links.Single().Url);
        Assert.AreEqual("access", item.Links.Single().AccessCode);
        Assert.AreEqual("archive password", item.Links.Single().ArchivePassword);
        Assert.AreEqual(drive, item.Links.Single().DriveKind);
    }

    private async Task<AcquisitionRequestedPayload> StartWithCachedLead(AcquisitionLead lead, string step)
    {
        await _sp.GetRequiredService<IAiFeatureService>().DeleteConfigAsync(AiFeature.PostParser);
        await _sp.GetRequiredService<IAiFeatureService>().DeleteConfigAsync(AiFeature.Default);
        var definition = await _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(new()
        {
            Name = "Use parsed links", TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
            Activities = [new WorkflowActivityInputModel {Kind = step}]
        });
        var task = await _sp.GetRequiredService<IAcquisitionService>().CreateAsync(lead.ResourceId, lead.Kind,
            lead.Value, lead.Id, definition.Id);
        Assert.IsNotNull(task.WorkflowRunId);
        var run = await Db.Set<WorkflowRunDbModel>().AsNoTracking()
            .SingleAsync(r => r.Id == task.WorkflowRunId);
        return JsonSerializer.Deserialize<AcquisitionRequestedPayload>(run.PayloadJson!, Json)!;
    }

    private async Task AssertCachedResolution(AcquisitionRequestedPayload payload, string code, string password)
    {
        var item = (AcquisitionWorkItem) new AcquisitionRequestedTrigger().ExtractItems(payload).Single();
        Assert.AreEqual(code, item.Links.Single().AccessCode);
        Assert.AreEqual(password, item.Links.Single().ArchivePassword);
        var step = new ResolveSharedContentStep();
        var issues = await step.ValidateConfigurationAsync(new AcquisitionValidationContext(_sp, null,
            true, item.LeadKind, item.LeadValue, payload.InitialLinks), CancellationToken.None);
        Assert.AreEqual(0, issues.Count);
        var outcome = await step.ExecuteAsync(new AcquisitionStepContext(_sp, NullLogger.Instance,
            (_, _) => Task.CompletedTask, ""), item, CancellationToken.None);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        Assert.AreSame(item, ((AcquisitionStepOutcome.Continue) outcome).Item);
    }

    private sealed class UnexpectedReader : IPostContentService
    {
        public bool CanRead(string reference, string? sourceHint = null) =>
            throw new AssertFailedException("Importing a parsed result must not read the original post again.");
        public Task<PostContent> ReadAsync(string reference, string? sourceHint = null,
            CancellationToken ct = default) => throw new AssertFailedException("No fetching during import.");
    }

    private sealed class UnexpectedExtractor : IPostDownloadInfoExtractor
    {
        public Task<PostDownloadInfo> ExtractAsync(PostContent content, CancellationToken ct = default) =>
            throw new AssertFailedException("The existing parsed links must be reused without an AI call.");
    }
}
