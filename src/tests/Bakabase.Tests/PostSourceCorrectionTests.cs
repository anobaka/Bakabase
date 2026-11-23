using System.Security.Cryptography;
using System.Buffers;
using System.Text;
using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Service.Components.Acquisition.Handoffs;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostSourceCorrectionTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext Db => _services.GetRequiredService<BakabaseDbContext>();
    private PostAcquisitionPlanAdapter Adapter => _services.GetRequiredService<PostAcquisitionPlanAdapter>();
    private const string Image = "https://images.example.test/download-qr.png";
    private const string Corrected = "https://files.example.test/download.zip";
    private const string Html = "<p>Main download</p><img src='https://images.example.test/download-qr.png'><p>Separate supplement below</p>";

    [TestInitialize]
    public async Task Setup() => _services = await TestServiceBuilder.BuildServiceProvider(services =>
    {
        services.RemoveAll<DownloadMethodRegistry>();
        services.AddScoped(_ => new DownloadMethodRegistry([], [], null!, null!));
    });

    [TestCleanup]
    public void Cleanup() => (_services as IDisposable)?.Dispose();

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private async Task<PostParserTaskDbModel> Saved()
    {
        var row = new PostParserTaskDbModel
        {
            Title = "Source evidence", Link = "https://example.test/post", Revision = 4,
            ParsingState = "complete", WorkflowRunId = 987654,
            ContentSnapshotJson = JsonSerializer.Serialize(new PostContent {MainHtml = Html}, JsonSerializerOptions.Web),
            Results = JsonSerializer.Serialize(new {DownloadInfo = new {isComplete = true,
                groups = new[] {new {id = "main", title = "Main", kind = "main"}, new {id = "supplement", title = "Supplement", kind = "supplement"}},
                resources = new object[]
                {
                    new {link = ">null", code = "old-code", password = "archive-secret", groupId = "main", extraction = new {requirement = "notRequired", steps = Array.Empty<object>()}},
                    new {link = "https://files.example.test/supplement.zip", code = "supplement-code", groupId = "supplement", extraction = new {requirement = "notRequired", steps = Array.Empty<object>()}}
                }}})
        };
        Db.PostParserTasks.Add(row); await Db.SaveChangesAsync();
        Db.Set<AcquisitionHandoffDbModel>().Add(new() {Id = $"postParser:task:{row.Id}:4", SourceModule = "postParser", SourceKind = "task", SourceId = row.Id.ToString(), SourceRevision = "4", ProducerRunId = row.WorkflowRunId.Value, Status = "error", Code = "SOURCE_INVALID", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now});
        await Db.SaveChangesAsync(); return row;
    }

    private static PostDownloadIntentRequest Input(PostParserTaskDbModel row) => new()
    {
        TaskId = row.Id, Revision = row.Revision, ResourceIndices = [0],
        ExpectedResultsHash = PostAcquisitionResultFingerprint.Hash(row.Results),
        CandidateSourceCorrections = new() {{"0", new()
        {
            ResourceIndex = 0, ExpectedOriginalUrl = ">null", Url = Corrected, AccessCode = "reviewed-code",
            ImageScope = "mainHtml", ImageIndex = 0, ImageUrl = Image, SourceHtmlSha256 = Hash(Html),
            ImageSha256 = Hash("reviewed image bytes"), Codec = "qr", DecodedValue = Corrected
        }}}
    };

    [TestMethod]
    public async Task CorrectionFreezesEvidenceAndPreservesProducerResultHandoffAndSupplement()
    {
        var row = await Saved(); var original = row.Results; var snapshot = row.ContentSnapshotJson;
        var result = await Adapter.CreateAsync(Input(row), Path.GetTempPath(), default);
        var task = await Db.AcquisitionTasks.AsNoTracking().SingleAsync();
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson!, JsonSerializerOptions.Web)!;
        var source = plan.Parts.Single().Sources.Single();
        Assert.AreEqual(Corrected, source.Url); Assert.AreEqual("reviewed-code", source.AccessCode);
        Assert.AreEqual("archive-secret", source.ArchivePassword);
        var evidence = source.SourceCorrection!.Value.GetProperty("evidence");
        Assert.AreEqual("userReviewedQr", evidence.GetProperty("kind").GetString());
        Assert.AreEqual(Hash(Html), evidence.GetProperty("sourceHtmlSha256").GetString());
        Assert.AreEqual("0", evidence.GetProperty("source").GetProperty("resultKey").GetString());
        var unchanged = await Db.PostParserTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(original, unchanged.Results); Assert.AreEqual(snapshot, unchanged.ContentSnapshotJson);
        Assert.AreEqual(4, unchanged.Revision); Assert.AreEqual(987654, unchanged.WorkflowRunId);
        var handoff = await Db.Set<AcquisitionHandoffDbModel>().AsNoTracking().SingleAsync();
        Assert.AreEqual("error", handoff.Status); Assert.AreEqual("SOURCE_INVALID", handoff.Code); Assert.IsNull(handoff.AcquisitionTaskId);
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync()); Assert.AreEqual(1, result.AcquisitionTaskIds.Count);
        Assert.IsTrue(task.PlanHistoryJson!.Contains("userReviewedQr", StringComparison.Ordinal));
        Assert.IsFalse(task.PlanJson!.Contains("supplement.zip", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ExactRepeatedCorrectionReusesTheSameTaskAndDifferentEvidenceConflicts()
    {
        var row = await Saved(); var input = Input(row);
        var first = await Adapter.CreateAsync(input, Path.GetTempPath(), default);
        var again = await Adapter.CreateAsync(input, Path.GetTempPath(), default);
        Assert.AreEqual(first.AcquisitionTaskIds.Single(), again.AcquisitionTaskIds.Single());
        var otherDirectory = Path.Combine(Path.GetTempPath(), "another-target");
        var differentDestination = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Adapter.CreateAsync(input, otherDirectory, default));
        StringAssert.Contains(differentDestination.Message, "SOURCE_CORRECTION_EXISTING_ACQUISITION");
        var changed = input with {CandidateSourceCorrections = new() {{"0", input.CandidateSourceCorrections["0"] with {ImageSha256 = Hash("different image")}}}};
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Adapter.CreateAsync(changed, Path.GetTempPath(), default));
        StringAssert.Contains(error.Message, "SOURCE_CORRECTION_EXISTING_ACQUISITION");
        StringAssert.Contains(error.Message, first.AcquisitionTaskIds.Single().ToString());
        Assert.AreEqual(1, await Db.AcquisitionTasks.CountAsync());
    }

    [TestMethod]
    public async Task ExistingUncorrectedPlanConflictsInsteadOfCreatingAnotherTask()
    {
        var row = await Saved();
        await _services.GetRequiredService<AcquisitionPlanService>().CreatePreparedPlanAsync(new()
        {
            Title = "Existing", OutputDirectory = Path.GetTempPath(), Parts = [new()
            {Id = "part", SelectedSourceId = "0", Sources = [new() {Id = "0", Url = "https://files.example.test/other.zip"}], Processing = new() {Requirement = "notRequired"}}]
        }, [new WorkflowSourceRef("postParser", "task", row.Id.ToString(), "4")]);
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Adapter.CreateAsync(Input(row), Path.GetTempPath(), default));
        StringAssert.Contains(error.Message, "SOURCE_CORRECTION_EXISTING_ACQUISITION");
        Assert.AreEqual(1, await Db.AcquisitionTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("revision")][DataRow("resultsHash")][DataRow("original")][DataRow("selection")]
    [DataRow("decoded")][DataRow("urlScheme")][DataRow("imageUrl")][DataRow("imageIndex")]
    [DataRow("htmlHash")][DataRow("imageHash")][DataRow("commentScope")][DataRow("codec")]
    public async Task StaleOrUnboundEvidenceCannotCreateAnAcquisition(string change)
    {
        var row = await Saved(); var input = Input(row); var value = input.CandidateSourceCorrections["0"];
        switch (change)
        {
            case "revision": input = input with {Revision = 3}; break;
            case "resultsHash": input = input with {ExpectedResultsHash = Hash("changed")}; break;
            case "original": value = value with {ExpectedOriginalUrl = "null"}; break;
            case "selection": input = input with {ResourceIndices = [1]}; break;
            case "decoded": value = value with {DecodedValue = "https://files.example.test/another.zip"}; break;
            case "urlScheme": value = value with {Url = "file:///etc/passwd", DecodedValue = "file:///etc/passwd"}; break;
            case "imageUrl": value = value with {ImageUrl = "https://images.example.test/not-present.png"}; break;
            case "imageIndex": value = value with {ImageIndex = 1}; break;
            case "htmlHash": value = value with {SourceHtmlSha256 = Hash("changed HTML")}; break;
            case "imageHash": value = value with {ImageSha256 = "invalid"}; break;
            case "commentScope": value = value with {ImageScope = "commentHtmlList", CommentIndex = 0}; break;
            case "codec": value = value with {Codec = "guess"}; break;
        }
        input = input with {CandidateSourceCorrections = new() {{"0", value}}};
        try {await Adapter.CreateAsync(input, Path.GetTempPath(), default); Assert.Fail("Invalid evidence was accepted.");}
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
        Assert.AreEqual(0, await Db.AcquisitionTasks.CountAsync()); Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task RawResultHashDetectsWhitespaceChangesAndDirectDownloadCannotDiscardEvidence()
    {
        var row = await Saved(); var input = Input(row); row.Results += " "; await Db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Adapter.CreateAsync(input, Path.GetTempPath(), default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _services.GetRequiredService<PostDownloadIntentAdapter>().SubmitAsync(input, Path.GetTempPath(), default));
        Assert.AreEqual(0, await Db.AcquisitionTasks.CountAsync());
    }

    private static Newtonsoft.Json.JsonSerializerSettings HttpJson() => new()
    {
        ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
        {
            NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy {ProcessDictionaryKeys = false}
        },
        DateFormatString = "yyyy-MM-dd HH:mm:ss.fff",
        ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore
    };

    [TestMethod]
    public async Task HttpGetFormatterExposesSavedCorrectionAndHistoryInsteadOfValueKind()
    {
        var row = await Saved();
        var created = await Adapter.CreateAsync(Input(row), Path.GetTempPath(), default);
        var persisted = await Db.AcquisitionTasks.AsNoTracking().SingleAsync();
        var savedPlan = persisted.PlanJson; var savedHistory = persisted.PlanHistoryJson;
        using var stored = JsonDocument.Parse(savedPlan!);
        var expected = Newtonsoft.Json.Linq.JToken.Parse(stored.RootElement.GetProperty("parts")[0]
            .GetProperty("sources")[0].GetProperty("sourceCorrection").GetRawText());
        await using var stream = new MemoryStream();
        var http = new DefaultHttpContext(); http.Response.Body = stream;
        var controller = new AcquisitionPlansController(_services.GetRequiredService<AcquisitionPlanService>(), null!)
            {ControllerContext = new ControllerContext {HttpContext = http}};
        var response = await controller.Get(created.AcquisitionTaskIds.Single());
        // Exercise the actual MVC Newtonsoft output formatter used by this GET,
        // after a STJ database reload, rather than the internal persistence serializer.
        var formatter = new NewtonsoftJsonOutputFormatter(HttpJson(), ArrayPool<char>.Shared,
            new MvcOptions {SuppressOutputFormatterBuffering = true}, new MvcNewtonsoftJsonOptions());
        var context = new OutputFormatterWriteContext(http, (output, encoding) => new StreamWriter(output, encoding),
            response.GetType(), response);
        await formatter.WriteResponseBodyAsync(context, new UTF8Encoding(false));
        var json = Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(stream.ToArray()));
        var actual = json["data"]!["plan"]!["parts"]![0]!["sources"]![0]!["sourceCorrection"]!;
        Assert.IsTrue(Newtonsoft.Json.Linq.JToken.DeepEquals(expected, actual));
        Assert.IsNull(actual["valueKind"]);
        Assert.IsTrue(Newtonsoft.Json.Linq.JToken.DeepEquals(expected,
            json["data"]!["planHistory"]![0]!["plan"]!["parts"]![0]!["sources"]![0]!["sourceCorrection"]));
        var untouched = await Db.AcquisitionTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(savedPlan, untouched.PlanJson); Assert.AreEqual(savedHistory, untouched.PlanHistoryJson);
    }

    [TestMethod]
    public void HttpCorrectionRoundTripPreservesNestedValuesAndDateLookingEvidenceStrings()
    {
        const string value = """
            {"fingerprint":"abc","evidence":{"url":"https://example.test/a?x=1&y=2","password":"2026-10-24T01:02:03Z","text":"引号\"和\\","flags":[true,17,null],"optional":null}}
            """;
        using var parsed = JsonDocument.Parse(value);
        var source = new AcquisitionPlanSource {SourceCorrection = parsed.RootElement.Clone()};
        var wire = Newtonsoft.Json.JsonConvert.SerializeObject(source, HttpJson());
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<AcquisitionPlanSource>(wire, HttpJson())!;
        Assert.IsTrue(Newtonsoft.Json.Linq.JToken.DeepEquals(Newtonsoft.Json.Linq.JToken.Parse(value),
            Newtonsoft.Json.Linq.JToken.Parse(restored.SourceCorrection!.Value.GetRawText())));
        Assert.AreEqual(JsonValueKind.String, restored.SourceCorrection.Value.GetProperty("evidence").GetProperty("password").ValueKind);
        var candidate = new DownloadCandidate {SourceCorrection = parsed.RootElement.Clone()};
        var candidateWire = Newtonsoft.Json.JsonConvert.SerializeObject(candidate, HttpJson());
        var candidateRestored = Newtonsoft.Json.JsonConvert.DeserializeObject<DownloadCandidate>(candidateWire, HttpJson())!;
        Assert.IsTrue(Newtonsoft.Json.Linq.JToken.DeepEquals(Newtonsoft.Json.Linq.JToken.Parse(value),
            Newtonsoft.Json.Linq.JToken.Parse(candidateRestored.SourceCorrection!.Value.GetRawText())));
        Assert.IsNull(Newtonsoft.Json.JsonConvert.DeserializeObject<AcquisitionPlanSource>("{\"sourceCorrection\":null}", HttpJson())!.SourceCorrection);
    }
}
