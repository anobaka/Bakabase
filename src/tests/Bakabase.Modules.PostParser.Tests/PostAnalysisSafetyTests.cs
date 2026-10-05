using System.Net;
using System.Text.Json;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.PostParser.Tests;

[TestClass]
public class PostAnalysisSafetyTests
{
    [TestMethod]
    public async Task ExtractionPreservesThreeOrderedRenameAndPasswordRounds()
    {
        var llm = new FakeLlm("""
            {"resources":[{"link":"https://pan.baidu.com/s/example","code":"abcd","extraction":{
              "requirement":"required","evidence":["改成7z后用密码one解压"],"steps":[
              {"id":"r1","op":"renameExtension","input":"download","selector":"*.bin","extension":".7z"},
              {"id":"x1","op":"extractArchive","input":"r1","password":"one"},
              {"id":"r2","op":"renameExtension","input":"x1","selector":"*.dat","extension":".zip"},
              {"id":"x2","op":"extractArchive","input":"r2","password":"two"},
              {"id":"r3","op":"renameExtension","input":"x2","extension":".rar"},
              {"id":"x3","op":"extractArchive","input":"r3","password":"three"}]}}]}
            """);
        var result = await new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance)
            .ExtractAsync(new PostContent {MainHtml = "Instructions", Locks = [new("https://example.test/buy", 1, false)]});
        Assert.IsFalse(result.IsComplete);
        Assert.AreEqual(2, result.SchemaVersion);
        var steps = result.Resources.Single().Extraction!.Steps;
        Assert.AreEqual(6, steps.Count);
        CollectionAssert.AreEqual(new[] {"one", "two", "three"}, steps.Where(s => s.Op == "extractArchive").Select(s => s.Password).ToArray());
        Assert.AreEqual("x2", steps[4].Input);
    }

    [TestMethod]
    public void LegacyStepsDoNotGainNullTargetFieldsThatWouldChangeSavedPlanIdentity()
    {
        var json = JsonSerializer.Serialize(new PostExtractionStep {Id = "open", Op = "extractArchive", Password = "same"},
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        Assert.IsFalse(document.RootElement.TryGetProperty("targetName", out _));
        Assert.IsFalse(document.RootElement.TryGetProperty("targetDirectory", out _));
        Assert.AreEqual("same", document.RootElement.GetProperty("password").GetString());
    }

    [TestMethod]
    public async Task ExtractionPreservesRenameAndMoveInstructionsWithoutRequiringAnArchive()
    {
        var llm = new FakeLlm("""
            {"resources":[{"link":"https://example.test/file","extraction":{
              "requirement":"required","steps":[
                {"id":"name","op":"renameFile","input":"download","selector":"notes.bin","targetName":"notes.txt"},
                {"id":"place","op":"moveFile","input":"name","targetDirectory":"documents/notes"}]}}]}
            """);
        var result = await new PostDownloadInfoExtractor(llm, NullLogger<PostDownloadInfoExtractor>.Instance)
            .ExtractAsync(new PostContent {MainHtml = "把notes.bin改名notes.txt后移动到documents/notes"});
        var steps = result.Resources.Single().Extraction!.Steps;
        Assert.AreEqual("renameFile", steps[0].Op);
        Assert.AreEqual("notes.txt", steps[0].TargetName);
        Assert.AreEqual("moveFile", steps[1].Op);
        Assert.AreEqual("documents/notes", steps[1].TargetDirectory);
        Assert.AreEqual("name", steps[1].Input);
        StringAssert.Contains(llm.Prompt!, "不必包含解压步骤");
    }

    [TestMethod]
    [DataRow("renameFile", "../outside.txt")]
    [DataRow("renameFile", "dir/file.txt")]
    [DataRow("renameFile", "NUL.txt")]
    [DataRow("renameFile", "file.")]
    [DataRow("moveFile", "../outside")]
    [DataRow("moveFile", "/outside")]
    [DataRow("moveFile", "C:\\outside")]
    [DataRow("moveFile", "a/../outside")]
    [DataRow("moveFile", "a//b")]
    public void RenameAndMoveTargetsMustStayWithinTheProcessingOutput(string operation, string target)
    {
        var step = new PostExtractionStep {Id = "change", Op = operation,
            TargetName = operation == "renameFile" ? target : null,
            TargetDirectory = operation == "moveFile" ? target : null};
        Assert.ThrowsExactly<InvalidOperationException>(() => PostExtractionPlanValidator.Validate(
            new() {Requirement = "required", Steps = [step]}));
    }

    [TestMethod]
    public void MovingToTheOutputRootIsAnExplicitSupportedRelativeDestination()
    {
        var plan = PostExtractionPlanValidator.Validate(new() {Requirement = "required", Steps = [
            new() {Id = "move", Op = "moveFile", TargetDirectory = "."}]});
        Assert.AreEqual(".", plan!.Steps.Single().TargetDirectory);
    }

    [TestMethod]
    [DataRow("notRequired")]
    [DataRow("unknown")]
    public void EmptyPlansDistinguishUnneededExtractionFromUnknownInstructions(string requirement)
    {
        var plan = PostExtractionPlanValidator.Validate(new() {Requirement = requirement});
        Assert.AreEqual(requirement, plan!.Requirement);
        Assert.AreEqual(0, plan.Steps.Count);
    }

    [TestMethod]
    public void PlanRejectsUnknownOperationsForwardInputsAndTraversal()
    {
        foreach (var step in new[]
        {
            new PostExtractionStep {Id = "x", Op = "shell", Input = "download"},
            new PostExtractionStep {Id = "x", Op = "extractArchive", Input = "later"},
            new PostExtractionStep {Id = "x", Op = "extractArchive", Selector = "../outside"},
            new PostExtractionStep {Id = "x", Op = "renameExtension", Extension = ".7z;rm"}
        })
            Assert.ThrowsExactly<InvalidOperationException>(() => PostExtractionPlanValidator.Validate(new() {Requirement = "required", Steps = [step]}));
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("{\"status\":\"valid\"}")]
    [DataRow("{\"status\":\"expired\",\"evidence\":[]}")]
    [DataRow("{\"status\":\"restored\",\"evidence\":[]}")]
    public async Task UnreliableAvailabilityResponsesStayUnknown(string response)
    {
        var result = await new PostAvailabilityAnalyzer(new FakeLlm(response)).AnalyzeAsync(new PostContent());
        Assert.AreEqual("unknown", result.Status);
    }

    [TestMethod]
    public async Task AvailabilityReceivesCommentIdentityAndCaptureScope()
    {
        var llm = new FakeLlm("{\"status\":\"restored\",\"evidence\":[\"reuploaded\"]}");
        var result = await new PostAvailabilityAnalyzer(llm).AnalyzeAsync(new PostContent
        {
            Comments = [new() {Id = "read_12", Floor = "12", Author = "author", Html = "reuploaded"}]
        });
        Assert.AreEqual("restored", result.Status);
        StringAssert.Contains(llm.Prompt!, "read_12");
        StringAssert.Contains(llm.Prompt!, "firstPage");
    }

    [TestMethod]
    public async Task HallucinatedRestorationEvidenceCannotAuthorizeAutomaticPurchase()
    {
        var result = await new PostAvailabilityAnalyzer(new FakeLlm("{\"status\":\"restored\",\"evidence\":[\"reuploaded\"]}"))
            .AnalyzeAsync(new PostContent {MainHtml = "This link expired."});
        Assert.AreEqual("unknown", result.Status);
    }

    [TestMethod]
    public async Task NewExtractionWithoutInstructionsHasAnExplicitUnknownPlan()
    {
        var result = await new PostDownloadInfoExtractor(new FakeLlm("{\"resources\":[{\"link\":\"https://example.test/file\"}]}"),
            NullLogger<PostDownloadInfoExtractor>.Instance).ExtractAsync(new PostContent());
        Assert.AreEqual("unknown", result.Resources.Single().Extraction!.Requirement);
    }

    [TestMethod]
    [DataRow("http://pan.baidu.com/s/test")]
    [DataRow("https://pan.baidu.com.evil.test/s/test")]
    [DataRow("https://127.0.0.1/test")]
    [DataRow("https://pan.baidu.com:8443/s/test")]
    [DataRow("https://user:pass@pan.baidu.com/s/test")]
    [DataRow("https://unsupported.example/file")]
    public async Task UnsupportedOrUnsafeLinksNeverSendRequests(string url)
    {
        var factory = new FakeClients(_ => new(HttpStatusCode.OK));
        Assert.AreEqual("unknown", (await new PostLinkHealthChecker(factory).CheckAsync(url, null)).Status);
        Assert.AreEqual(0, factory.Calls);
    }

    [TestMethod]
    [DataRow("<div>分享的文件已经被取消</div>", "unavailable")]
    [DataRow("<script>var message='分享的文件已经被取消';</script><div>输入提取码</div>", "unknown")]
    [DataRow("<div>请完成验证</div>", "unknown")]
    public async Task BaiduOnlyMarksExplicitVisibleUnavailableEvidence(string html, string expected)
    {
        var factory = new FakeClients(_ => new(HttpStatusCode.OK) {Content = new StringContent(html)});
        Assert.AreEqual(expected, (await new PostLinkHealthChecker(factory).CheckAsync("https://pan.baidu.com/s/test", "abcd")).Status);
    }

    [TestMethod]
    [DataRow("[-9]", "unavailable")]
    [DataRow("[-11]", "unknown")]
    [DataRow("[{\"s\":123,\"at\":\"encoded\"}]", "available")]
    public async Task MegaChecksMetadataWithoutRequestingDownloadUrls(string body, string expected)
    {
        var factory = new FakeClients(request =>
        {
            Assert.AreEqual("g.api.mega.co.nz", request.RequestUri!.Host);
            Assert.AreEqual(HttpMethod.Post, request.Method);
            return new(HttpStatusCode.OK) {Content = new StringContent(body)};
        });
        var url = "https://mega.nz/file/abcdefgh#" + new string('a', 43);
        Assert.AreEqual(expected, (await new PostLinkHealthChecker(factory).CheckAsync(url, null)).Status);
    }

    [TestMethod]
    public async Task RedirectToPrivateAddressIsNotFollowed()
    {
        var factory = new FakeClients(_ => new(HttpStatusCode.Redirect) {Headers = {Location = new Uri("https://127.0.0.1/private")}});
        Assert.AreEqual("unknown", (await new PostLinkHealthChecker(factory).CheckAsync("https://1drv.ms/test", null)).Status);
        Assert.AreEqual(1, factory.Calls);
    }

    [TestMethod]
    [DataRow("127.0.0.1")]
    [DataRow("10.0.0.1")]
    [DataRow("172.16.1.2")]
    [DataRow("192.168.1.1")]
    [DataRow("169.254.169.254")]
    [DataRow("::1")]
    [DataRow("::ffff:127.0.0.1")]
    [DataRow("fd00::1")]
    public void NetworkProbeRejectsNonPublicDestinations(string ip) =>
        Assert.IsFalse(PostLinkHealthChecker.IsPublicAddress(IPAddress.Parse(ip)));

    private sealed class FakeClients(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public int Calls;
        public HttpClient CreateClient(string name) => new(new Handler(request => { Calls++; return respond(request); }));
        private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
        }
    }

    private sealed class FakeLlm(string response) : ILlmService
    {
        public string? Prompt;
        public Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parametersOverride = null, CancellationToken ct = default)
        {
            Prompt = string.Join("\n", messages.Select(m => m.Text));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }
        public Task<ChatResponse> CompleteAsync(int providerConfigId, string modelId, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature feature, IList<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
