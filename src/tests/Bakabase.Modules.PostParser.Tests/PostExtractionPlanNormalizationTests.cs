using System.Text.Json;
using Bakabase.Modules.AI.Components.Cache;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using CachingChatClient = Bakabase.Modules.AI.Components.Cache.CachingChatClient;

namespace Bakabase.Modules.PostParser.Tests;

[TestClass]
public class PostExtractionPlanNormalizationTests
{
    [TestMethod]
    [DataRow("rar", ".rar")]
    [DataRow("7z", ".7z")]
    [DataRow("tar.gz", ".tar.gz")]
    [DataRow("tar.gz.x", ".tar.gz.x")]
    [DataRow("ZIP", ".ZIP")]
    [DataRow("abcdefghijklmnop", ".abcdefghijklmnop")]
    public void PlainAndDottedExtensionsHaveTheSameCanonicalIdempotentValue(string bare, string canonical)
    {
        Assert.AreEqual(canonical, FileProcessingExtensionNormalizer.Normalize(bare));
        Assert.AreEqual(canonical, FileProcessingExtensionNormalizer.Normalize(canonical));
        Assert.AreEqual(canonical, FileProcessingExtensionNormalizer.Normalize(FileProcessingExtensionNormalizer.Normalize(bare)));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("rar ")]
    [DataRow(" rar")]
    [DataRow(" .rar")]
    [DataRow(".rar ")]
    [DataRow("rar\n")]
    [DataRow("rar\0")]
    [DataRow("../rar")]
    [DataRow("..rar")]
    [DataRow("/rar")]
    [DataRow("\\rar")]
    [DataRow("a/rar")]
    [DataRow("a\\rar")]
    [DataRow("C:rar")]
    [DataRow("tar..gz")]
    [DataRow("tar.gz.")]
    [DataRow("tar.gz.x.y")]
    [DataRow(".")]
    [DataRow("..")]
    [DataRow("7z;rm")]
    [DataRow("7z&x")]
    [DataRow("压缩")]
    [DataRow("+zip")]
    [DataRow("a_b")]
    [DataRow("a b")]
    [DataRow("abcdefghijklmnopq")]
    public void MissingUnsafeWhitespaceAndOverlongExtensionsRemainRejected(string? value)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => FileProcessingExtensionNormalizer.Normalize(value));
        Assert.ThrowsExactly<InvalidOperationException>(() => PostExtractionPlanValidator.Validate(new()
        {
            Requirement = "required", Steps = [new() {Id = "rename", Op = "renameExtension", Extension = value}]
        }));
    }

    [TestMethod]
    public void GenericValidatorStaysStrictEvenWhenAConsumerIgnoresItsReturnValue()
    {
        var plan = new FileProcessingPlan
        {
            Requirement = "required", Steps = [new() {Id = "rename", Op = "renameExtension", Extension = "rar"}]
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => FileProcessingPlanValidator.Validate(plan));
        Assert.AreEqual("rar", plan.Steps[0].Extension);
    }

    [TestMethod]
    public void PostBoundaryPreservesSubtypesInstructionsAndOriginalObjects()
    {
        var rename = new SpecializedStep
        {
            Id = "rename", Op = "renameExtension", Input = "download", Selector = "*.bin",
            Extension = "tar.gz", Annotation = "retained"
        };
        var extract = new PostExtractionStep
        {
            Id = "open", Op = "extractArchive", Input = "rename", Password = "  literal  secret  "
        };
        var plan = new SpecializedPlan
        {
            Requirement = "required", Steps = [rename, extract], Evidence = ["Explicit supplied instructions"],
            Annotation = "retained"
        };
        var original = JsonSerializer.Serialize(plan);

        var normalized = PostExtractionPlanValidator.Validate(plan)!;

        Assert.IsInstanceOfType<SpecializedPlan>(normalized);
        Assert.IsInstanceOfType<SpecializedStep>(normalized.Steps[0]);
        Assert.AreEqual("retained", ((SpecializedPlan)normalized).Annotation);
        Assert.AreEqual("retained", ((SpecializedStep)normalized.Steps[0]).Annotation);
        Assert.AreNotSame(plan, normalized);
        Assert.AreNotSame(plan.Steps, normalized.Steps);
        Assert.AreNotSame(rename, normalized.Steps[0]);
        Assert.AreEqual(original, JsonSerializer.Serialize(plan));
        Assert.AreEqual("tar.gz", rename.Extension);
        Assert.AreEqual(".tar.gz", normalized.Steps[0].Extension);
        Assert.AreEqual("*.bin", normalized.Steps[0].Selector);
        Assert.AreEqual("rename", normalized.Steps[1].Input);
        Assert.AreEqual(extract.Password, normalized.Steps[1].Password);
        CollectionAssert.AreEqual(plan.Evidence, normalized.Evidence);
        Assert.AreEqual(JsonSerializer.Serialize(normalized), JsonSerializer.Serialize(PostExtractionPlanValidator.Validate(normalized)));
        FileProcessingPlanValidator.Validate(normalized);
    }

    [TestMethod]
    public void MissingPlanOperationsInputsAndPasswordsAreNeverInvented()
    {
        Assert.IsNull(PostExtractionPlanValidator.Validate(null));
        var unknown = PostExtractionPlanValidator.Validate(new())!;
        Assert.AreEqual("unknown", unknown.Requirement);
        Assert.AreEqual(0, unknown.Steps.Count);
        var renameOnly = PostExtractionPlanValidator.Validate(new()
        {
            Requirement = "required", Steps = [new() {Id = "r", Op = "renameExtension", Extension = "7z"}]
        })!;
        Assert.AreEqual(1, renameOnly.Steps.Count);
        Assert.IsNull(renameOnly.Steps[0].Password);
        Assert.IsNull(renameOnly.Steps[0].TargetName);
        Assert.ThrowsExactly<InvalidOperationException>(() => PostExtractionPlanValidator.Validate(new()
        {
            Requirement = "required", Steps = [new() {Id = "r", Op = "renameExtension", Input = "later", Extension = "7z"}]
        }));
        Assert.ThrowsExactly<InvalidOperationException>(() => PostExtractionPlanValidator.Validate(new() {Steps = null!}));
        Assert.ThrowsExactly<InvalidOperationException>(() => PostExtractionPlanValidator.Validate(new() {Steps = [null!]}));
    }

    [TestMethod]
    public async Task CachedAiAnswerIsNormalizedAtTheExtractorBoundaryWithoutRewritingItsCache()
    {
        const string answer = """
            {"resources":[{"link":"https://example.test/archive","code":" unchanged ","password":" unchanged ",
              "extraction":{"requirement":"required","evidence":["Provided instructions"],"steps":[
                {"id":"r","op":"renameExtension","input":"download","extension":"tar.gz"},
                {"id":"x","op":"extractArchive","input":"r","password":"  exact  password  "}]}}]}
            """;
        var native = new JsonClient(answer);
        var cache = new MemoryCache();
        using var client = new CachingChatClient(native, cache, 1, "fixture", new());
        var extractor = new PostDownloadInfoExtractor(new CachedLlm(client), NullLogger<PostDownloadInfoExtractor>.Instance);
        var content = new PostContent {MainHtml = "Fixture instructions"};

        var first = await extractor.ExtractAsync(content);
        var cached = await extractor.ExtractAsync(content);

        Assert.AreEqual(1, native.Calls, "The second answer must come from the actual LLM response cache.");
        Assert.AreEqual(1, cache.SetCount);
        Assert.IsTrue(cache.Hits > 0);
        foreach (var resource in new[] {first.Resources.Single(), cached.Resources.Single()})
        {
            Assert.AreEqual(".tar.gz", resource.Extraction!.Steps[0].Extension);
            Assert.AreEqual("r", resource.Extraction.Steps[1].Input);
            Assert.AreEqual("  exact  password  ", resource.Extraction.Steps[1].Password);
            Assert.AreEqual(" unchanged ", resource.Code);
            Assert.AreEqual(" unchanged ", resource.Password);
            FileProcessingPlanValidator.Validate(resource.Extraction);
        }
        var originalCache = JsonSerializer.Deserialize<ChatResponse>(cache.Entry!.ResponseJson, JsonSerializerOptions.Web)!;
        using var originalAnswer = JsonDocument.Parse(originalCache.Text);
        Assert.AreEqual("tar.gz", originalAnswer.RootElement.GetProperty("resources")[0]
            .GetProperty("extraction").GetProperty("steps")[0].GetProperty("extension").GetString());
    }

    [TestMethod]
    [DataRow("renameExtension", null)]
    [DataRow("renameExtension", "")]
    [DataRow("renameFile", null)]
    [DataRow("renameFile", "")]
    [DataRow("moveFile", null)]
    [DataRow("moveFile", "")]
    [DataRow("renameLiteral.find", null)]
    [DataRow("renameLiteral.find", "")]
    [DataRow("renameLiteral.replace", null)]
    public void MissingOperationParametersKeepEvidenceButNoExecutablePrefix(string operation, string? value)
    {
        var step = new PostExtractionStep {Id = "missing", Input = "open", Op = operation.Split('.')[0],
            Extension = operation == "renameExtension" ? value : null,
            TargetName = operation == "renameFile" ? value : null,
            TargetDirectory = operation == "moveFile" ? value : null,
            FindText = operation == "renameLiteral.find" ? value : "literal",
            ReplaceText = operation == "renameLiteral.replace" ? value : ""};
        var original = new PostExtractionPlan {Requirement = "required", Evidence = ["A change is needed, but its target is not specified."],
            Steps = [new() {Id = "open", Op = "extractArchive"}, step]};
        var before = JsonSerializer.Serialize(original);

        var safe = PostExtractionPlanValidator.ValidateAnalysis(original, out var incomplete)!;

        Assert.IsTrue(incomplete);
        Assert.AreEqual("required", safe.Requirement);
        Assert.AreEqual(0, safe.Steps.Count, "No prefix of an incomplete plan may execute.");
        CollectionAssert.AreEqual(original.Evidence, safe.Evidence);
        Assert.AreEqual(before, JsonSerializer.Serialize(original));
        FileProcessingPlanValidator.Validate(safe);
        Assert.ThrowsExactly<InvalidOperationException>(() => FileProcessingPlanValidator.Validate(original));
    }

    [TestMethod]
    public void MissingParameterCannotHideUnsafeValuesOrInvalidStructureAnywhereInThePlan()
    {
        foreach (var invalid in new FileProcessingStep[]
        {
            new() {Id = "unsafe", Op = "renameExtension", Extension = " 7z"},
            new() {Id = "unsafe", Op = "renameExtension", Extension = "../rar"},
            new() {Id = "unsafe", Op = "renameFile", TargetName = "folder/file.txt"},
            new() {Id = "unsafe", Op = "moveFile", TargetDirectory = "../other"},
            new() {Id = "unsafe", Op = "renameLiteral", FindText = null, ReplaceText = "../other"},
            new() {Id = "unsafe", Op = "renameLiteral", FindText = "*", ReplaceText = null},
            new() {Id = "unsafe", Op = "extractArchive", Selector = "../outside"},
            new() {Id = "unsafe", Op = "extractArchive", Input = "later"},
            new() {Id = "unsafe", Op = "shell"},
            new() {Id = "unsafe", Op = ""},
            new() {Id = "", Op = "extractArchive"}
        })
            Assert.ThrowsExactly<InvalidOperationException>(() => PostExtractionPlanValidator.ValidateAnalysis(new()
            {
                Requirement = "required", Steps = [new() {Id = "missing", Op = "renameFile"}, invalid]
            }, out _), invalid.Op);
    }

    [TestMethod]
    public void OptionalSelectorAndPasswordAndExplicitLiteralRemovalAreNotMissingParameters()
    {
        var safe = PostExtractionPlanValidator.ValidateAnalysis(new() {Requirement = "required", Steps = [
            new() {Id = "remove", Op = "renameLiteral", FindText = "remove", ReplaceText = ""},
            new() {Id = "open", Op = "extractArchive", Input = "remove"}]}, out var incomplete)!;
        Assert.IsFalse(incomplete);
        Assert.AreEqual(2, safe.Steps.Count);
        Assert.AreEqual("", safe.Steps[0].ReplaceText);
        Assert.IsNull(safe.Steps[1].Password);
    }

    [TestMethod]
    public async Task IncompleteCachedAnalysisRetainsResourcesCredentialsAndEvidenceWithoutRewritingResponse()
    {
        const string answer = """
            {"resources":[{"link":"https://example.test/archive","code":" literal-code ","password":" literal-password ",
              "extraction":{"requirement":"required","evidence":["Rename the downloaded file, then extract it three times."],"steps":[
                {"id":"rename","op":"renameExtension","input":"download","extension":null},
                {"id":"open","op":"extractArchive","input":"rename","password":" per-step value "}]}},
              {"link":"https://example.test/another","extraction":{"requirement":"required","steps":[
                {"id":"move","op":"moveFile","input":"download","targetDirectory":null}]}}]}
            """;
        var native = new JsonClient(answer);
        var cache = new MemoryCache();
        using var client = new CachingChatClient(native, cache, 1, "fixture", new());
        var extractor = new PostDownloadInfoExtractor(new CachedLlm(client), NullLogger<PostDownloadInfoExtractor>.Instance);
        var content = new PostContent {MainHtml = "Rename the downloaded file, then extract it three times."};
        var first = await extractor.ExtractAsync(content);
        var second = await extractor.ExtractAsync(content);

        Assert.AreEqual(1, native.Calls);
        Assert.AreEqual(1, cache.SetCount);
        foreach (var result in new[] {first, second})
        {
            Assert.IsTrue(result.IsComplete);
            Assert.AreEqual(2, result.Resources.Count);
            Assert.AreEqual("https://example.test/archive", result.Resources[0].Link);
            Assert.AreEqual(" literal-code ", result.Resources[0].Code);
            Assert.AreEqual(" literal-password ", result.Resources[0].Password);
            Assert.AreEqual("required", result.Resources[0].Extraction!.Requirement);
            Assert.IsTrue(result.Resources.All(r => r.Extraction!.Steps.Count == 0));
            Assert.AreEqual("Rename the downloaded file, then extract it three times.", result.Resources[0].Extraction!.Evidence.Single());
            CollectionAssert.AreEqual(new[] {PostExtractionPlanValidator.IncompletePlanWarning}, result.Warnings);
        }
        var retained = JsonSerializer.Deserialize<ChatResponse>(cache.Entry!.ResponseJson, JsonSerializerOptions.Web)!;
        Assert.AreEqual(answer, retained.Text, "The raw incomplete proposal, including its per-step credentials, remains untouched.");
    }

    private sealed record SpecializedPlan : PostExtractionPlan { public string? Annotation { get; init; } }
    private sealed record SpecializedStep : PostExtractionStep { public string? Annotation { get; init; } }

    private sealed class MemoryCache : ILlmCacheService
    {
        public LlmCallCacheEntryDbModel? Entry;
        public int SetCount;
        public int Hits;
        public Task<LlmCallCacheEntryDbModel?> GetAsync(string cacheKey, CancellationToken ct = default)
        {
            if (Entry?.CacheKey != cacheKey) return Task.FromResult<LlmCallCacheEntryDbModel?>(null);
            Hits++;
            return Task.FromResult<LlmCallCacheEntryDbModel?>(Entry);
        }
        public Task SetAsync(LlmCallCacheEntryDbModel entry, CancellationToken ct = default)
        { Entry = entry; SetCount++; return Task.CompletedTask; }
        public Task<IReadOnlyList<LlmCallCacheEntryDbModel>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ClearAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task CleanupExpiredAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class JsonClient(string answer) : IChatClient
    {
        public int Calls;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer))); }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class CachedLlm(IChatClient client) : ILlmService
    {
        public Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parametersOverride = null, CancellationToken ct = default) =>
            client.GetResponseAsync(messages, new() {ResponseFormat = ChatResponseFormat.Json}, ct);
        public Task<ChatResponse> CompleteAsync(int providerConfigId, string modelId, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages, LlmModelParameters? parameters = null,
            AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature feature, IList<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
